#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#endif

#include <algorithm>
#include <cctype>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <regex>
#include <set>
#include <string>
#include <utility>
#include <vector>

namespace fs = std::filesystem;

struct InputData
{
    std::string value;
    std::string option;
    bool isNumeric = false;
};

static bool isSpace(char c) { return std::isspace(static_cast<unsigned char>(c)) != 0; }

static std::string ltrim(const std::string& s)
{
    size_t i = 0;
    while (i < s.size() && isSpace(s[i])) i++;
    return s.substr(i);
}

static std::string rtrim(const std::string& s)
{
    size_t n = s.size();
    while (n > 0 && isSpace(s[n - 1])) n--;
    return s.substr(0, n);
}

static std::string trim(const std::string& s) { return rtrim(ltrim(s)); }

static bool startsWith(const std::string& s, const std::string& prefix)
{
    return s.size() >= prefix.size() && s.compare(0, prefix.size(), prefix) == 0;
}

static std::string toLower(std::string s)
{
    for (char& c : s) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return s;
}

static std::string removeChar(const std::string& s, char ch)
{
    std::string out;
    out.reserve(s.size());
    for (char c : s)
        if (c != ch) out += c;
    return out;
}

static bool equalsStr(const std::string& a, const std::string& b, bool ignoreCase)
{
    return ignoreCase ? toLower(a) == toLower(b) : a == b;
}

static std::string regexEscape(const std::string& s)
{
    static const std::string special = R"(.^$|()[]{}*+?\)";
    std::string out;
    for (char c : s)
    {
        if (special.find(c) != std::string::npos) out += '\\';
        out += c;
    }
    return out;
}

static bool isInteger(const std::string& s)
{
    size_t i = 0;
    if (i < s.size() && (s[i] == '+' || s[i] == '-')) i++;
    if (i >= s.size()) return false;
    for (size_t k = i; k < s.size(); k++)
        if (!std::isdigit(static_cast<unsigned char>(s[k]))) return false;
    try { (void)std::stoi(s); } catch (...) { return false; }
    return true;
}

static std::vector<std::string> splitAll(const std::string& s, const std::string& delim)
{
    std::vector<std::string> parts;
    size_t pos = 0;
    while (true)
    {
        size_t next = s.find(delim, pos);
        if (next == std::string::npos)
        {
            parts.push_back(s.substr(pos));
            break;
        }
        parts.push_back(s.substr(pos, next - pos));
        pos = next + delim.size();
    }
    return parts;
}

static std::vector<std::string> readAllLines(const std::string& path)
{
    std::vector<std::string> lines;
    std::ifstream in(path, std::ios::binary);
    std::string line;
    while (std::getline(in, line))
    {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        lines.push_back(line);
    }
    return lines;
}

static bool writeAllLines(const std::string& path, const std::vector<std::string>& lines)
{
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) return false;
#ifdef _WIN32
    const char* eol = "\r\n";
#else
    const char* eol = "\n";
#endif
    for (const auto& l : lines) out << l << eol;
    return static_cast<bool>(out);
}

static std::string executableDirectory(const char* argv0)
{
#ifdef _WIN32
    (void)argv0;
    char buf[MAX_PATH];
    DWORD n = GetModuleFileNameA(nullptr, buf, MAX_PATH);
    std::string path(buf, n);
    size_t slash = path.find_last_of("\\/");
    return slash == std::string::npos ? "." : path.substr(0, slash);
#else
    try
    {
        return fs::read_symlink("/proc/self/exe").parent_path().string();
    }
    catch (...)
    {
        fs::path p(argv0);
        return p.has_parent_path() ? p.parent_path().string() : ".";
    }
#endif
}

struct Updates
{
    std::map<std::string, std::vector<InputData>> data;
    std::vector<std::pair<std::string, std::string>> order;
};

static std::string normalizeKey(const std::string& s, bool ignoreCase)
{
    return ignoreCase ? toLower(s) : s;
}

static Updates parseInputFile(const std::string& inputPath, bool ignoreCase)
{
    Updates updates;
    int lineNumber = 0;

    for (std::string line : readAllLines(inputPath))
    {
        lineNumber++;
        if (lineNumber == 1 && startsWith(line, "\xEF\xBB\xBF")) line = line.substr(3); // UTF-8 BOM
        if (trim(line).empty()) continue;

        std::vector<std::string> parts;
        if (line.find('|') != std::string::npos)
            parts = splitAll(line, "|");
        else if (line.find(" - ") != std::string::npos)
            parts = splitAll(line, " - ");

        if (parts.size() == 2)
        {
            std::string question = trim(parts[0]);
            std::string data = trim(parts[1]);

            InputData input;
            if (isInteger(data))
            {
                input.isNumeric = true;
                input.value = data;
                input.option = data;
            }
            else
            {
                input.option = data;
            }

            std::string key = normalizeKey(question, ignoreCase);
            if (updates.data.find(key) == updates.data.end())
                updates.order.emplace_back(key, question);
            updates.data[key].push_back(input);
        }
        else
        {
            std::cout << "Warning: Invalid format in input.txt at line " << lineNumber
                      << ": '" << line << "'\n";
        }
    }
    return updates;
}

static bool applyValue(std::vector<std::string>& block, const std::string& newValue)
{
    static const std::regex re(R"((Value\s*=\s*)(<)?(\d+)(>)?)");

    for (auto& line : block)
    {
        if (!startsWith(trim(line), "Value")) continue;

        std::smatch m;
        std::string old;
        if (std::regex_search(line, m, re))
        {
            old = m[3].str();
            line = m.prefix().str() + m[1].str() + (m[2].matched ? "<" : "") + newValue +
                   (m[4].matched ? ">" : "") + m.suffix().str();
        }
        std::cout << "  - Updated value from " << old << " to " << newValue << "\n";
        return true;
    }
    return false;
}

static bool applyOption(std::vector<std::string>& block, const std::string& target,
                        bool isIndex, bool ignoreCase)
{
    int start = -1;
    for (size_t k = 0; k < block.size(); k++)
        if (startsWith(ltrim(block[k]), "Options")) { start = static_cast<int>(k); break; }
    if (start < 0) return false;

    int end = start;
    while (end + 1 < static_cast<int>(block.size()) && !trim(block[end + 1]).empty()) end++;

    auto flags = std::regex::ECMAScript;
    if (ignoreCase) flags |= std::regex::icase;

    static const std::regex optionsRe(R"((Options\s*=\s*)(.*))");
    static const std::regex unmarkRe(R"(^(\s*)\*)");
    const std::regex indexRe("\\[" + regexEscape(target) + "\\]", flags);
    const std::regex labelRe(R"(\[[0-9A-Fa-f]+\](.+))", flags);

    std::map<int, std::string> newLines;
    bool found = false;

    for (int k = start; k <= end; k++)
    {
        const std::string& line = block[k];
        if (k > start && startsWith(ltrim(line), "//")) continue; // keep comment lines untouched

        size_t c = line.find("//");
        std::string code = c != std::string::npos ? line.substr(0, c) : line;
        std::string comment = c != std::string::npos ? line.substr(c) : "";

        std::string labelPart = trim(removeChar(code, '*'));

        bool isMatch = false;
        if (isIndex)
        {
            isMatch = std::regex_search(labelPart, indexRe);
        }
        else
        {
            std::smatch m;
            if (std::regex_search(labelPart, m, labelRe))
                isMatch = equalsStr(trim(m[1].str()), target, ignoreCase);
        }
        found |= isMatch;

        if (k == start)
        {
            std::string cleaned = removeChar(code, '*');
            std::smatch em;
            if (std::regex_search(cleaned, em, optionsRe))
                newLines[k] = em.prefix().str() + em[1].str() + (isMatch ? "*" : "") +
                              em[2].str() + comment;
            else
                newLines[k] = cleaned + comment;
        }
        else
        {
            std::string unmarked = std::regex_replace(code, unmarkRe, "$1 ");
            std::string content = ltrim(unmarked);
            std::string indent = unmarked.substr(0, unmarked.size() - content.size());

            if (isMatch)
                indent = indent.empty() ? "*" : indent.substr(0, indent.size() - 1) + "*";

            newLines[k] = indent + content + comment;
        }
    }

    if (!found) return false;
    for (const auto& kv : newLines) block[kv.first] = kv.second;
    return true;
}

static void modifyNvram(const std::string& inputPath, const std::string& nvramPath, bool ignoreCase)
{
    if (!fs::exists(nvramPath))
    {
        std::cout << "Error: nvram.txt file not found.\n";
        return;
    }
    if (!fs::exists(inputPath))
    {
        std::cout << "Error: input.txt file not found.\n";
        return;
    }

    Updates updates = parseInputFile(inputPath, ignoreCase);
    std::vector<std::string> lines;
    std::vector<std::string> nvramLines = readAllLines(nvramPath);
    std::set<std::string> foundQuestions;
    std::set<std::string> modifiedQuestions;

    std::cout << "\nProcessing NVRAM modifications...\n";
    std::cout << "=================================\n";
    if (ignoreCase) std::cout << "Case-insensitive matching enabled\n";

    static const std::regex questionRe(R"(Setup Question\s*=\s*(.+))");

    for (size_t i = 0; i < nvramLines.size(); i++)
    {
        const std::string& line = nvramLines[i];
        lines.push_back(line);

        if (!startsWith(line, "Setup Question")) continue;

        std::smatch match;
        if (!std::regex_search(line, match, questionRe)) continue;

        std::string setupQuestion = trim(match[1].str());
        std::string key = normalizeKey(setupQuestion, ignoreCase);
        auto it = updates.data.find(key);
        if (it == updates.data.end()) continue;

        foundQuestions.insert(key);
        std::cout << "\nModifying Setup Question: " << setupQuestion << "\n";

        std::vector<std::string> block;
        size_t j = i + 1;
        while (j < nvramLines.size() && !startsWith(nvramLines[j], "Setup Question"))
            block.push_back(nvramLines[j++]);

        bool hasOptions = false, hasValue = false;
        for (const auto& l : block)
        {
            if (startsWith(ltrim(l), "Options")) hasOptions = true;
            if (startsWith(trim(l), "Value")) hasValue = true;
        }

        for (const auto& update : it->second)
        {
            if (update.isNumeric)
            {
                if (hasOptions)
                {
                    std::string idx = update.option.size() == 1 ? "0" + update.option : update.option;
                    if (applyOption(block, idx, true, ignoreCase) ||
                        applyOption(block, update.option, false, ignoreCase))
                    {
                        std::cout << "  - Selected option: " << update.option << "\n";
                        modifiedQuestions.insert(key);
                    }
                    else
                    {
                        std::cout << "  - Warning: Option '" << update.option
                                  << "' not found in options list\n";
                    }
                }
                else if (hasValue)
                {
                    if (applyValue(block, update.value)) modifiedQuestions.insert(key);
                }
                else
                {
                    std::cout << "  - Warning: '" << setupQuestion
                              << "' has neither Options nor Value\n";
                }
                continue;
            }

            if (applyOption(block, update.option, false, ignoreCase))
            {
                std::cout << "  - Selected option: " << update.option << "\n";
                modifiedQuestions.insert(key);
            }
            else
            {
                std::cout << "  - Warning: Option '" << update.option
                          << "' not found in options list\n";
            }
        }

        lines.insert(lines.end(), block.begin(), block.end());
        i = j - 1;
    }

    std::vector<std::string> missing;
    for (const auto& kv : updates.order)
        if (foundQuestions.find(kv.first) == foundQuestions.end()) missing.push_back(kv.second);

    if (!missing.empty())
    {
        std::cout << "\nSetup Questions not found in nvram.txt:\n";
        for (const auto& q : missing) std::cout << "  - " << q << "\n";
    }

    if (!writeAllLines(nvramPath, lines))
    {
        std::cout << "Error: could not write nvram.txt.\n";
        return;
    }

    std::cout << "\nNVRAM update completed successfully.\n";
    std::cout << "Found " << foundQuestions.size() << " questions (" << modifiedQuestions.size()
              << " modified), " << missing.size() << " questions not found.\n";
}

int main(int argc, char* argv[])
{
    bool ignoreCase = false;
    for (int i = 1; i < argc; i++)
        if (std::string(argv[i]) == "--ignore-case") ignoreCase = true;

    if (ignoreCase) std::cout << "Case-insensitive mode enabled.\n";

    std::string baseDirectory = executableDirectory(argv[0]);
    std::string inputPath = (fs::path(baseDirectory) / "input.txt").string();
    std::string nvramPath = (fs::path(baseDirectory) / "nvram.txt").string();

    modifyNvram(inputPath, nvramPath, ignoreCase);
    return 0;
}
using System.Text.RegularExpressions;

class NvramModifier
{
    public class InputData
    {
        public string? Value { get; set; }
        public string? Option { get; set; }
        public bool IsOptionIndex { get; set; }
    }

    private static Dictionary<string, List<InputData>> ParseInputFile(string inputPath)
    {
        var updates = new Dictionary<string, List<InputData>>();
        int lineNumber = 0;

        foreach (var line in File.ReadAllLines(inputPath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[]? parts = null;
            if (line.Contains("|"))
                parts = line.Split('|');
            else if (line.Contains(" - "))
                parts = line.Split(new[] { " - " }, StringSplitOptions.None);

            if (parts?.Length == 2)
            {
                string question = parts[0].Trim();
                string data = parts[1].Trim();

                var inputData = new InputData();

                if (int.TryParse(data, out _))
                {
                    if (data.Length == 2)
                        inputData.Option = data;
                    else
                        inputData.Value = data;
                }
                else
                {
                    inputData.Option = data;
                }

                inputData.IsOptionIndex = int.TryParse(data, out _);

                if (!updates.ContainsKey(question))
                    updates[question] = new List<InputData>();

                updates[question].Add(inputData);
            }
            else
            {
                Console.WriteLine($"Warning: Invalid format in input.txt at line {lineNumber}: '{line}'");
            }
        }
        return updates;
    }

    private static void ModifyNvram(string inputPath, string nvramPath, bool ignoreCase = false)
    {
        if (!File.Exists(nvramPath))
        {
            Console.WriteLine("Error: nvram.txt file not found.");
            return;
        }
        if (!File.Exists(inputPath))
        {
            Console.WriteLine("Error: input.txt file not found.");
            return;
        }

        var updates = ParseInputFile(inputPath);
        var lines = new List<string>();
        var nvramLines = File.ReadAllLines(nvramPath);
        var processedQuestions = new HashSet<string>();
        var foundQuestions = new HashSet<string>();
        var modifiedQuestions = new HashSet<string>();

        var questionLookup = ignoreCase 
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();

        if (ignoreCase)
        {
            foreach (var key in updates.Keys)
            {
                questionLookup[key] = key;
            }
        }

        Console.WriteLine("\nProcessing NVRAM modifications...");
        Console.WriteLine("=================================");
        if (ignoreCase)
        {
            Console.WriteLine("Case-insensitive matching enabled");
        }

        for (int i = 0; i < nvramLines.Length; i++)
        {
            string line = nvramLines[i];

            if (line.StartsWith("Setup Question"))
            {
                var match = Regex.Match(line, @"Setup Question\s*=\s*(.+)");
                if (match.Success)
                {
                    string setupQuestion = match.Groups[1].Value.Trim();
                    lines.Add(line);

                    string? matchingKey = null;
                    List<InputData>? questionUpdates = null;

                    if (ignoreCase)
                    {
                        var matchingEntry = updates.FirstOrDefault(kvp => 
                            string.Equals(kvp.Key, setupQuestion, StringComparison.OrdinalIgnoreCase));
                        
                        if (!matchingEntry.Equals(default(KeyValuePair<string, List<InputData>>)))
                        {
                            matchingKey = matchingEntry.Key;
                            questionUpdates = matchingEntry.Value;
                        }
                    }
                    else
                    {
                        if (updates.ContainsKey(setupQuestion))
                        {
                            matchingKey = setupQuestion;
                            questionUpdates = updates[setupQuestion];
                        }
                    }

                    if (matchingKey != null && questionUpdates != null)
                    {
                        foundQuestions.Add(matchingKey);
                        var updateQueue = new Queue<InputData>(questionUpdates);
                        Console.WriteLine($"\nModifying Setup Question: {setupQuestion}");

                        while (updateQueue.Count > 0)
                        {
                            var updateData = updateQueue.Dequeue();
                            int originalIndex = i;

                            if (updateData.Value != null)
                            {
                                while (i + 1 < nvramLines.Length && !nvramLines[i + 1].StartsWith("Setup Question"))
                                {
                                    i++;
                                    string currentLine = nvramLines[i];

                                    if (currentLine.Trim().StartsWith("Value"))
                                    {
                                        string oldValue = Regex.Match(currentLine, @"Value\s*=\s*<?(\d+)>?").Groups[1].Value;
                                        string updatedLine = Regex.Replace(currentLine,
                                            @"(Value\s*=\s*)(<)?(\d+)(>)?",
                                            m => {
                                                string prefix = m.Groups[2].Success ? "<" : "";
                                                string suffix = m.Groups[4].Success ? ">" : "";
                                                return $"{m.Groups[1].Value}{prefix}{updateData.Value}{suffix}";
                                            });

                                        Console.WriteLine($"  - Updated value from {oldValue} to {updateData.Value}");
                                        modifiedQuestions.Add(matchingKey);
                                        lines.Add(updatedLine);
                                        break;
                                    }
                                    else
                                    {
                                        lines.Add(currentLine);
                                    }
                                }
                            }

                            if (updateData.Option != null)
                            {
                                i = originalIndex;
                                while (i + 1 < nvramLines.Length && !nvramLines[i + 1].StartsWith("Options"))
                                {
                                    i++;
                                    lines.Add(nvramLines[i]);
                                }
                                i++;

                                if (i < nvramLines.Length)
                                {
                                    string optionsLine = nvramLines[i];
                                    i++;

                                    var optionsSection = new List<string>();
                                    optionsSection.Add(optionsLine.Replace("*", "").TrimEnd());

                                    while (i < nvramLines.Length && !string.IsNullOrWhiteSpace(nvramLines[i]))
                                    {
                                        if (!nvramLines[i].StartsWith("//"))
                                        {
                                            optionsSection.Add(nvramLines[i].Replace("*", "").TrimEnd());
                                        }
                                        i++;
                                    }

                                    bool optionFound = false;
                                    bool firstLine = true;
                                    string targetOption = updateData.Option;

                                    foreach (string optionLine in optionsSection)
                                    {
                                        bool isMatch = false;
                                        if (updateData.IsOptionIndex)
                                        {
                                            RegexOptions regexOptions = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                                            isMatch = Regex.IsMatch(optionLine, @"\[" + Regex.Escape(updateData.Option) + @"\]", regexOptions);
                                        }
                                        else
                                        {
                                            string labelPart = optionLine.Split(new[] { "//" }, 2, StringSplitOptions.None)[0].Trim();

                                            RegexOptions regexOptions = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                                            var labelMatch = Regex.Match(labelPart, @"\[[0-9A-Fa-f]+\](.+)", regexOptions);
                                            if (labelMatch.Success)
                                            {
                                                string label = labelMatch.Groups[1].Value.Trim();
                                                StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                                                isMatch = label.Equals(targetOption, comparison);
                                            }
                                        }

                                        if (isMatch)
                                        {
                                            optionFound = true;
                                        }

                                        if (firstLine)
                                        {
                                            var equalsMatch = Regex.Match(optionLine, @"(Options\s*=\s*)(.*)");
                                            if (equalsMatch.Success)
                                            {
                                                string prefix = equalsMatch.Groups[1].Value;
                                                string remainder = equalsMatch.Groups[2].Value;
                                                lines.Add($"{prefix}{(isMatch ? "*" : "")}{remainder}");
                                            }
                                            else
                                            {
                                                lines.Add(optionLine);
                                            }
                                            firstLine = false;
                                        }
                                        else
                                        {
                                            string indent = Regex.Match(optionLine, @"^\s*").Value;
                                            string content = optionLine.TrimStart();
                                            lines.Add($"{indent}{(isMatch ? "*" : "")}{content}");
                                        }
                                    }

                                    if (!optionFound)
                                    {
                                        Console.WriteLine($"  - Warning: Option '{updateData.Option}' not found in options list");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"  - Selected option: {updateData.Option}");
                                        modifiedQuestions.Add(matchingKey);
                                    }
                                    i--;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                lines.Add(line);
            }
        }

        var missingQuestions = updates.Keys.Except(foundQuestions);
        if (missingQuestions.Any())
        {
            Console.WriteLine("\nSetup Questions not found in nvram.txt:");
            foreach (var question in missingQuestions)
            {
                Console.WriteLine($"  - {question}");
            }
        }

        File.WriteAllLines(nvramPath, lines);
        Console.WriteLine("\nNVRAM update completed successfully.");
        Console.WriteLine($"Found {foundQuestions.Count} questions ({modifiedQuestions.Count} modified), {missingQuestions.Count()} questions not found.");
    }

    public static void Main(string[] args)
    {
        bool ignoreCase = args.Contains("--ignore-case");
        
        if (ignoreCase)
        {
            Console.WriteLine("Case-insensitive mode enabled.");
        }

        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string inputPath = Path.Combine(baseDirectory, "input.txt");
        string nvramPath = Path.Combine(baseDirectory, "nvram.txt");

        ModifyNvram(inputPath, nvramPath, ignoreCase);
    }
}

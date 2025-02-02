using System.Text.RegularExpressions;

class NvramModifier
{
    public class InputData
    {
        public string? Value { get; set; }
        public string? Option { get; set; }
        public bool IsOptionIndex { get; set; }
    }

    private static Dictionary<string, InputData> ParseInputFile(string inputPath)
    {
        var updates = new Dictionary<string, InputData>();
        foreach (var line in File.ReadAllLines(inputPath))
        {
            string[]? parts = null;
            if (line.Contains("|"))
                parts = line.Split('|');
            else if (line.Contains(" - "))
                parts = line.Split(new[] { " - " }, StringSplitOptions.None);

            if (parts?.Length == 2)
            {
                string question = parts[0].Trim();
                string data = parts[1].Trim();

                if (int.TryParse(data, out _))
                {
                    if (data.Length == 2)
                    {
                        updates[question] = new InputData
                        {
                            Option = data,
                            IsOptionIndex = true
                        };
                    }
                    else
                    {
                        updates[question] = new InputData { Value = data };
                    }
                }
                else
                {
                    updates[question] = new InputData
                    {
                        Option = data,
                        IsOptionIndex = false
                    };
                }
            }
        }
        return updates;
    }

    private static void ModifyNvram(string inputPath, string nvramPath)
    {
        if (!File.Exists(nvramPath) || !File.Exists(inputPath))
        {
            Console.WriteLine("Error: One or both files are missing.");
            return;
        }

        var updates = ParseInputFile(inputPath);
        var lines = new List<string>();
        var nvramLines = File.ReadAllLines(nvramPath);

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

                    if (updates.ContainsKey(setupQuestion))
                    {
                        var updateData = updates[setupQuestion];

                        if (updateData.Value != null)
                        {
                            bool emptyLineAdded = false;

                            while (i + 1 < nvramLines.Length && !nvramLines[i + 1].StartsWith("Setup Question"))
                            {
                                i++;
                                string currentLine = nvramLines[i];

                                if (currentLine.Trim().StartsWith("Value"))
                                {
                                    string updatedLine = Regex.Replace(currentLine,
                                        @"(Value\s*=\s*)(<)?(\d+)(>)?",
                                        m => {
                                            string prefix = m.Groups[2].Success ? "<" : "";
                                            string suffix = m.Groups[4].Success ? ">" : "";
                                            return $"{m.Groups[1].Value}{prefix}{updateData.Value}{suffix}";
                                        });

                                    lines.Add(updatedLine);
                                }
                                else if (!string.IsNullOrWhiteSpace(currentLine))
                                {
                                    lines.Add(currentLine);
                                    emptyLineAdded = false;
                                }
                                else if (!emptyLineAdded)
                                {
                                    lines.Add("");
                                    emptyLineAdded = true;
                                }
                            }
                        }

                        if (updateData.Option != null)
                        {
                            while (i + 1 < nvramLines.Length && !nvramLines[i + 1].StartsWith("Options"))
                            {
                                i++;
                                lines.Add(nvramLines[i]);
                            }
                            i++;

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

                            bool firstLine = true;
                            foreach (string optionLine in optionsSection)
                            {
                                bool isMatch = false;
                                if (updateData.IsOptionIndex)
                                {
                                    isMatch = Regex.IsMatch(optionLine, @"\[" + Regex.Escape(updateData.Option) + @"\]");
                                }
                                else
                                {
                                    isMatch = Regex.IsMatch(optionLine, @"\[\d+\]\s*" + Regex.Escape(updateData.Option));
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
                            i--;
                        }
                    }
                }
            }
            else
            {
                lines.Add(line);
            }
        }

        File.WriteAllLines(nvramPath, lines);
        Console.WriteLine("nvram.txt successfully updated.");
    }

    public static void Main()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string inputPath = Path.Combine(baseDirectory, "input.txt");
        string nvramPath = Path.Combine(baseDirectory, "nvram.txt");

        ModifyNvram(inputPath, nvramPath);
    }
}
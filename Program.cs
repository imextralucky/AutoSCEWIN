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
                        var questionUpdates = new Queue<InputData>(updates[setupQuestion]);

                        while (questionUpdates.Count > 0)
                        {
                            var updateData = questionUpdates.Dequeue();
                            int originalIndex = i;

                            if (updateData.Value != null)
                            {
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
# AutoSCEWIN
Tool to automate SCEWIN

## Usage
Parses `input.txt`, must be formatted as the following:

`Setup Question | Option`

`Setup Question - Option`

Place `AutoSCEWIN.exe` in the same directory as `input.txt` and `nvram.txt`

## Compile
`dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`

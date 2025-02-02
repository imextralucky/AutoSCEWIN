# AutoSCEWIN
Tool to automate SCEWIN

## Usage
Parses `input.txt`, must be formatted as the following:

`Setup Question | Option`

`Setup Question - Option`

Option can be either numerical value or text

Place `AutoSCEWIN.exe` in the same directory as `input.txt` and `nvram.txt`

Supports modifying the following:

```
Options	=*[00]Example
         [01]Example2

//

Value	=<0>

//

Value	=0
```

## Compile
`dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`

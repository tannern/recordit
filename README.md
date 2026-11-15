# Recordit

A small terminal application for tracking time worked on projects.

![Screenshot of record-it terminal application](screenshot.png)

### Commands

```text
recordit <project\>  
recordit <command\> [arguments]  
```

Arguments  
```text
<project\>                       Start tracking a project  
--summary                        Show total time for all projects  
--history <project\>             Show project session history  
--history <project\> <n\>        Show the last n sessions  
--delete <project\>              Delete all project sessions  
--delete <project\> <id\>        Delete a specific session  
--version, -v                    Show application version  
--help, -h                       Show this help message  
```

Press Q to stop the current session.

### Installation

1. Download the source to a folder of your choice.
2. From that folder location, in your terminal, type the following command:

```
dotnet publish -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o ./publish

mkdir -p ~/.local/bin
cp ./publish/recordit ~/.local/bin/
```

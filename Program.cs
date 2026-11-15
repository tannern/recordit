
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Recordit.Models;

class Program
{
    static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "recordit"
    );

    static readonly string DataFile = Path.Combine(
        DataDirectory,
        "sessions.json"
    );

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    static void Main(string[] args)
    {
        // No arguments: display help.
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        string command = args[0].ToLowerInvariant();

        // Help commands.
        if (command is "help" or "--help" or "-h")
        {
            PrintHelp();
            return;
        }

        // Version commands.
        if (command is "--version" or "-v")
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Error: --version does not accept arguments.");
                return;
            }

            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0] ?? "unknown";

            Console.WriteLine($"recordit {version}");
            return;
        }

        // Reject unknown options.
        if (command.StartsWith('-') &&
            command is not "--summary" and not "--history" and not "--delete")
        {
            Console.WriteLine($"Error: Unknown option '{args[0]}'.");
            Console.WriteLine("Run 'recordit --help' for usage information.");
            return;
        }

        // Validate command arguments.
        switch (command)
        {
            case "--summary":
                if (args.Length != 1)
                {
                    Console.WriteLine("Error: --summary does not accept arguments.");
                    return;
                }
                break;

            case "--history":
                if (args.Length < 2 || args.Length > 3)
                {
                    Console.WriteLine("Usage: recordit --history <project> [count]");
                    return;
                }
                break;

            case "--delete":
                if (args.Length < 2 || args.Length > 3)
                {
                    Console.WriteLine("Usage: recordit --delete <project> [session ID]");
                    return;
                }
                break;
        }

        // Load database only when a command needs it.
        SessionDatabase database;

        try
        {
            database = LoadSessions();
        }
        catch (Exception ex) when (
            ex is InvalidDataException || ex is IOException
        )
        {
            Console.WriteLine($"Storage error: {ex.Message}");
            return;
        }

        List<Session> sessions = database.Sessions;

        // Execute command.
        switch (command)
        {
            case "--summary":
                ShowSummary(sessions);
                break;

            case "--history":
                ShowHistory(args, sessions);
                break;

            case "--delete":
                DeleteSessions(args, database);
                break;

            default:
                StartTracking(string.Join(" ", args), database);
                break;
        }
    }
    
    static SessionDatabase LoadSessions()
    {
        if (!File.Exists(DataFile))
            return new SessionDatabase();

        string json = File.ReadAllText(DataFile);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            SessionDatabase database;

            // Migrate the old JSON array format.
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                database = new SessionDatabase
                {
                    Sessions =
                        JsonSerializer.Deserialize<List<Session>>(json) ?? []
                };
            }
            else
            {
                database =
                    JsonSerializer.Deserialize<SessionDatabase>(json)
                    ?? throw new JsonException("Invalid database.");
            }

            // Repair missing or duplicate IDs.
            HashSet<int> usedIds = [];
            int nextId = 1;

            foreach (Session session in database.Sessions)
            {
                if (session.Id > 0 && usedIds.Add(session.Id))
                    nextId = Math.Max(nextId, session.Id + 1);
            }

            for (int i = 0; i < database.Sessions.Count; i++)
            {
                Session session = database.Sessions[i];

                if (session.Id <= 0 || !usedIds.Remove(session.Id))
                {
                    while (!usedIds.Add(nextId))
                        nextId++;

                    database.Sessions[i] = session with { Id = nextId };
                    nextId++;
                }
            }

            database.NextSessionId = Math.Max(
                Math.Max(database.NextSessionId, nextId),
                1
            );

            return database;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Session database is corrupted: {ex.Message}", ex
            );
        }
    }

    static void SaveSessions(SessionDatabase database)
    {
        Directory.CreateDirectory(DataDirectory);

        string json = JsonSerializer.Serialize(database, JsonOptions);

        // Write to a temporary file first.
        string temporaryFile = DataFile + ".tmp";

        try
        {
            using (FileStream stream = new(
                temporaryFile,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            ))
            {
                using StreamWriter writer = new(stream);
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            // Replace the original file.
            File.Move(temporaryFile, DataFile, true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
    }

    // Start tracking a project.
    static void StartTracking(string taskName, SessionDatabase database)
    {
        DateTimeOffset startedAt = DateTimeOffset.Now;

        Stopwatch stopwatch = Stopwatch.StartNew();

        Console.Clear();
        Console.CursorVisible = false;
        
        int stopRequested = 0;
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            Interlocked.Exchange(ref stopRequested, 1);
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            while (Volatile.Read(ref stopRequested) == 0)
            {
                Console.SetCursorPosition(0, 0);

                Console.WriteLine($"Currently working on: {taskName}");

                Console.WriteLine(
                    $"This session: {FormatDuration(stopwatch.Elapsed)}"
                );

                Console.WriteLine();
                Console.WriteLine("Press Q to stop tracking.");

                if (Console.KeyAvailable)
                {
                    if (Console.ReadKey(true).Key == ConsoleKey.Q)
                        break;
                }

                Thread.Sleep(200);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            Console.CursorVisible = true;
        }

        stopwatch.Stop();

        Session session = new(
            database.NextSessionId++,
            taskName,
            startedAt,
            DateTimeOffset.Now,
            stopwatch.Elapsed
        );

        database.Sessions.Add(session);

        SaveSessions(database);

        Console.WriteLine();
        Console.WriteLine("Session ended.");
        Console.WriteLine($"Duration: {FormatDuration(session.Duration)}");
        Console.WriteLine("Session saved.");
    }

    // Show summary of all projects.
    static void ShowSummary(List<Session> sessions)
    {
        if (sessions.Count == 0)
        {
            Console.WriteLine("No recorded sessions.");
            return;
        }

        Console.WriteLine("Project Summary");
        Console.WriteLine("----------------------------------------");
        // Console.WriteLine("========================================");

        var projects = sessions
            .GroupBy(s => s.TaskName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        foreach (var project in projects)
        {
            TimeSpan total = TimeSpan.FromSeconds(
                project.Sum(s => s.Duration.TotalSeconds)
            );

            Console.WriteLine();
            Console.WriteLine(project.Key);
            Console.WriteLine($"  Total time: {FormatDuration(total)}");
            Console.WriteLine($"  Sessions:   {project.Count()}");
        }

        TimeSpan grandTotal = TimeSpan.FromSeconds(
            sessions.Sum(s => s.Duration.TotalSeconds)
        );

        Console.WriteLine();
        Console.WriteLine("----------------------------------------");
        // Console.WriteLine("========================================");
        Console.WriteLine($"Total tracked: {FormatDuration(grandTotal)}");
    }

    // Show history for one project.
    static void ShowHistory(string[] args, List<Session> sessions)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: recordit --history <project> [count]");
            return;
        }

        string projectName = args[1];

        int count = int.MaxValue;

        if (args.Length >= 3 &&
            (!int.TryParse(args[2], out count) || count <= 0))
        {
            Console.WriteLine("History count must be a positive integer.");
            return;
        }

        List<Session> projectSessions = sessions
            .Where(s => string.Equals(
                s.TaskName,
                projectName,
                StringComparison.OrdinalIgnoreCase
            ))
            .OrderByDescending(s => s.Id)
            .Take(count)
            .OrderBy(s => s.Id)
            .ToList();

        if (projectSessions.Count == 0)
        {
            Console.WriteLine($"No sessions found for {projectName}.");
            return;
        }

        Console.WriteLine($"Session History: {projectName}");
        Console.WriteLine("----------------------------------------------------------");

        foreach (Session session in projectSessions)
        {
            Console.WriteLine(
                $"[{session.Id}] {session.StartedAt:yyyy-MM-dd HH:mm} | " +
                $"{FormatDuration(session.Duration)}"
            );
        }
    }

    // Delete one session or an entire project.
    static void DeleteSessions(string[] args, SessionDatabase database)
    {
        List<Session> sessions = database.Sessions;
        
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: recordit --delete <project> [session ID]");
            return;
        }

        string projectName = args[1];

        if (args.Length >= 3)
        {
            if (!int.TryParse(args[2], out int sessionId))
            {
                Console.WriteLine("Invalid session ID.");
                return;
            }

            Session? session = sessions.FirstOrDefault(s =>
                s.Id == sessionId &&
                string.Equals(
                    s.TaskName,
                    projectName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (session is null)
            {
                Console.WriteLine("Session not found for that project.");
                return;
            }

            sessions.Remove(session);
            SaveSessions(database);

            Console.WriteLine($"Deleted session [{sessionId}] from {projectName}.");
            return;
        }

        List<Session> projectSessions = sessions
            .Where(s => string.Equals(
                s.TaskName,
                projectName,
                StringComparison.OrdinalIgnoreCase
            ))
            .ToList();

        if (projectSessions.Count == 0)
        {
            Console.WriteLine($"No sessions found for {projectName}.");
            return;
        }

        Console.WriteLine(
            $"Do you want to delete all {projectSessions.Count} sessions of {projectName}? (Y/N)"
        );

        if (Console.IsInputRedirected)
            return;

        ConsoleKeyInfo key = Console.ReadKey(true);

        if (key.Key != ConsoleKey.Y)
        {
            Console.WriteLine("Deletion cancelled.");
            return;
        }

        sessions.RemoveAll(s => string.Equals(
            s.TaskName,
            projectName,
            StringComparison.OrdinalIgnoreCase
        ));

        SaveSessions(database);

        Console.WriteLine($"All sessions for {projectName} have been deleted.");
    }

    // Format elapsed time.
    static string FormatDuration(TimeSpan duration)
    {
        return $"{(int)duration.TotalHours:00}:" +
               $"{duration.Minutes:00}:" +
               $"{duration.Seconds:00}";
    }

    static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("recordit - Simple project time tracking");
        Console.WriteLine();

        Console.WriteLine("USAGE");
        Console.WriteLine("  recordit <project>");
        Console.WriteLine("  recordit <command> [arguments]");
        Console.WriteLine();

        Console.WriteLine("COMMANDS");
        Console.WriteLine("  <project>                  Start tracking a project");
        Console.WriteLine("  --summary                  Show total time for all projects");
        Console.WriteLine("  --history <project>        Show project session history");
        Console.WriteLine("  --history <project> <n>    Show the last n sessions");
        Console.WriteLine("  --delete <project>         Delete all project sessions");
        Console.WriteLine("  --delete <project> <id>    Delete a specific session");
        Console.WriteLine("  --version, -v              Show application version");
        Console.WriteLine("  --help, -h                 Show this help message");
        Console.WriteLine();

        Console.WriteLine("EXAMPLES");
        Console.WriteLine("  recordit CodeSharp");
        Console.WriteLine("  recordit --summary");
        Console.WriteLine("  recordit --history CodeSharp");
        Console.WriteLine("  recordit --history CodeSharp 5");
        Console.WriteLine("  recordit --delete CodeSharp 19");
        Console.WriteLine("  recordit --delete CodeSharp");
        Console.WriteLine();

        Console.WriteLine("TRACKING");
        Console.WriteLine("  Press Q to stop the current session.");
        Console.WriteLine();

        Console.WriteLine("For more information, run: recordit --help");
        Console.WriteLine();
    }
}

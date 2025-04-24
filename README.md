# Taskmaster

**.NET 8 Job Control Daemon**

Taskmaster is a lightweight process supervisor inspired by Supervisor. It launches, monitors, and manages multiple programs defined in YAML, with support for automatic restarts, configurable policies, and multiple control interfaces.

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
  - [Interactive Shell](#interactive-shell)
  - [TCP Control Interface](#tcp-control-interface)
  - [HTTP REST API](#http-rest-api)
- [Building & Running](#building--running)
- [Demo Script](#demo-script)
- [Evaluation Tips](#evaluation-tips)
- [License](#license)

## Features

- YAML-based program definitions
- Automatic process supervision with restart policies (`always`, `never`, `unexpected`)
- Configurable options: `cmd`, `numprocs`, `autostart`, `exitcodes`, `starttime`, `startretries`, `stopsignal`, `stoptime`, stdout/stderr redirection, environment vars, workingdir, umask
- Multiple control interfaces:
  - Interactive shell with history and editing
  - TCP control server on `127.0.0.1:9090`
  - HTTP/JSON API on `http://localhost:8080/api`
- Graceful reload and shutdown via signals or commands
- Cross-platform support (Windows/Unix)
- Logging to file and colored console output

## Architecture

1. **ConfigurationManager**: loads and validates `taskmaster.yaml`.
2. **TaskmasterDaemon**: orchestrates startup, signal handlers, monitoring loop, and control servers.
3. **ProcessManager**: spawns and tracks `ProcessInfo` instances, enforces restart/back-off logic.
4. **CommandShell**: REPL with advanced `ReadLineWithHistory()`.
5. **Control Servers**: TCP on port 9090 and HTTP API on port 8080.

## Installation

Clone the repository:
```bash
git clone https://github.com/youruser/taskmaster.git
cd taskmaster
```

## Configuration

Edit `taskmaster.yaml` to define your programs:
```yaml
programs:
  webapp:
    cmd: "dotnet run --project WebApp"
    numprocs: 2
    autostart: true
    autorestart: unexpected
    exitcodes: [0]
    starttime: 5
    startretries: 3
    stopsignal: TERM
    stoptime: 10
    stdout: logs/webapp.stdout
    stderr: logs/webapp.stderr
    env:
      ASPNETCORE_ENVIRONMENT: Production
      CONNECTION_STRING: "Server=..."
```

## Usage

### Interactive Shell

```bash
dotnet run -- -c taskmaster.yaml
taskmaster> status
taskmaster> start webapp
taskmaster> reload
taskmaster> shutdown
```

Use arrow keys for command history and editing.

### TCP Control Interface

```bash
telnet 127.0.0.1 9090
> status
> start webapp
> exit
```

### HTTP REST API

```bash
# Get status JSON
curl http://localhost:8080/api/status

# Control a program
curl -X POST http://localhost:8080/api/programs/webapp/start

# Reload configuration
curl -X POST http://localhost:8080/api/reload

# Shutdown daemon
curl -X POST http://localhost:8080/api/shutdown
```

## Building & Running

Build in Release mode:
```bash
dotnet build -c Release
```

Run with custom config:
```bash
dotnet run -- -c path/to/taskmaster.yaml
```

Run in background (daemon) mode:
```bash
dotnet run -- -d
```

## Demo Script

1. **Launch Taskmaster**  
   ```bash
   dotnet run -- -c taskmaster.yaml
   ```  
   - In the shell, confirm autostarted programs:  
     ```bash
     taskmaster> status
     ```

2. **Simulate a Failure & Observe Restart**  
   - In another terminal, note a running PID:  
     ```bash
     taskmaster> status
     ```  
   - Kill that process:  
     ```bash
     # Unix
     kill <pid>
     # Windows
     taskkill /PID <pid> /F
     ```  
   - Back in Taskmaster shell, verify automatic restart:  
     ```bash
     taskmaster> status
     ```

3. **Interactive Shell Tasks**  
   ```bash
   taskmaster> stop webapp
   taskmaster> start webapp
   taskmaster> restart worker
   ```

4. **TCP Control Interface**  
   ```bash
   telnet 127.0.0.1 9090
   ```  
   Inside the session:
   ```
   > status
   > stop webapp
   > start webapp
   > exit
   ```

5. **HTTP/JSON API Calls**  
   ```bash
   # Get full status
   curl http://localhost:8080/api/status

   # Restart a program
   curl -X POST --data "" http://localhost:8080/api/programs/simple-web/restart
   ```

6. **Reload Configuration Without Downtime**  
   - Edit `taskmaster.yaml` (e.g. change `worker.numprocs` from 1 to 2).  
   - Save the file.  
   - Trigger reload in shell or via HTTP:  
     ```bash
     taskmaster> reload
     # or
     curl -X POST http://localhost:8080/api/reload
     ```  
   - Confirm new instances:  
     ```bash
     taskmaster> status
     ```

7. **Signal Handling**  
   - Reload via SIGHUP (Unix):  
     ```bash
     kill -HUP <daemon_pid>
     ```  
   - Dump status via SIGUSR1:  
     ```bash
     kill -USR1 <daemon_pid>
     ```  
   - Check the log file for the status dump entry.

8. **Graceful Shutdown**  
   - From the shell:  
     ```bash
     taskmaster> shutdown
     ```  
   - Or send Ctrl+C in the console, or via HTTP:  
     ```bash
     curl -X POST http://localhost:8080/api/shutdown
     ```

## Evaluation Tips

- **Slides**: diagram flow (Config → Daemon → ProcessManager → Interfaces).
- **Key code walkthrough**: `Program.cs`, `TaskmasterDaemon.cs`, `ProcessManager.cs`, `CommandShell.cs`.
- **Live demo**: follow the Demo Script above.
- **Be prepared to discuss** restart logic, back-off, non-disruptive reload, and cross-platform signal handling.

## License

[MIT](LICENSE)
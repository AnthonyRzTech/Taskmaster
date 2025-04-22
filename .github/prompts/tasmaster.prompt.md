<!-- filepath: c:\Users\Tony\Taskmaster\.github\prompts\tasmaster.prompt.md -->
# Taskmaster

**Target Platform:** .NET 8

**Summary:** The goal of this project is to make a job control daemon, with features similar to supervisor.

**Version:** 3

## Contents
I Foreword .............................................................................. 2
II Introduction ....................................................................... 3
III Goals ............................................................................ 4
IV General Instructions ............................................................... 5
IV.1 Language constraints ............................................................... 5
IV.2 Defense session ................................................................. 5
V Mandatory Part ...................................................................... 6
VI Bonus part ........................................................................ 8
VII Appendix ........................................................................ 9
VII.1 Example configuration file ...................................................... 9
VII.2 Trying out supervisor ........................................................ 10
VIII Submission and peer correction .................................................. 11

---

# Chapter I: Foreword
Here’s a small but useful piece of information on the Dwellers:

Picking a fight with a species as widespread, long-lived, irascible and (when
it suited them) single-minded as the Dwellers too often meant that just when
(or even geological ages after when) you thought that the dust had long since
settled, bygones were bygones and any unfortunate disputes were all ancient
history, a small planet appeared without warning in your home system,
accompanied by a fleet of moons, themselves surrounded with multitudes of
asteroid-sized chunks, each of those riding cocooned in a fuzzy shell made up
of untold numbers of decently hefty rocks, every one of them travelling
surrounded by a large landslide’s worth of still smaller rocks and pebbles, the
whole ghastly collection travelling at so close to the speed of light that the
amount of warning even an especially wary and observant species would have
generally amounted to just about sufficient time to gasp the local equivalent
of "What the fu--?" before they disappeared in an impressive, if wasteful,
blaze of radiation.

What are Dwellers? Google it! No, but seriously, go read *The Algebraist*. This
project is way easier if you have read it.

# Chapter II: Introduction
In Unix and Unix-like operating systems, job control refers to control of jobs by a shell,
especially interactively, where a "job" is a shell’s representation for a process group. Basic
job control features are the suspending, resuming, or terminating of all processes in the
job/process group; more advanced features can be performed by sending signals to the
job. Job control is of particular interest in Unix due to its multiprocessing, and should
be distinguished from job control generally, which is frequently applied to sequential
execution (batch processing).

# Chapter III: Goals
Your job here is to make a fully-fledged job control daemon. A pretty good example of
this would be supervisor.

For the sake of keeping it simple, your program will not run as root, and does not
HAVE to be a daemon. It will be started via shell, and do its job while providing a
control shell to the user.

# Chapter IV: General Instructions

## IV.1 Language constraints
Although you may choose any language, this implementation must target .NET 8. Libraries
are allowed for parsing configuration files and for bonus client/server features; beyond
that, restrict yourself to the language’s standard library.

## IV.2 Defense session
For the defense session, be prepared to:
- Demonstrate that your program correctly implements each required feature, using a configuration file you provide.
- Have your program tested by the grader in various ways: killing supervised processes, launching processes that never start, generating large output, etc.

# Chapter V: Mandatory Part
- Start jobs as child processes and keep them alive, restarting if necessary; track live/dead state accurately.
- Load program definitions from a configuration file (format of your choice; YAML recommended). Support reload via SIGHUP without despawning unchanged processes.
- Implement a logging system to a local file for events (start, stop, restart, unexpected death, config reload).
- Remain in the foreground and provide a control shell (line editing, history; completion optional) inspired by `supervisorctl`.

Control shell commands at minimum must allow the user to:
- View status of all programs (`status`)
- Start, stop, restart programs
- Reload configuration without stopping main program
- Stop the main program

Configuration file must support per-program options for:
- Launch command
- Number of processes to maintain
- Autostart on launch
- Restart policy (always, never, on unexpected exit)
- Expected exit codes
- Minimum run time to consider “started”
- Maximum restart attempts
- Stop signal for graceful exit
- Grace period before forced kill
- stdout/stderr discard or redirection
- Environment variables
- Working directory
- Umask

# Chapter VI: Bonus Part
Implement any supplemental feature for extra credit. Ideas:
- Privilege de-escalation on launch (requires root)
- Client/server architecture: separate daemon and control program communicating via sockets
- Advanced logging/reporting (email, HTTP, syslog)
- Attach/detach supervised processes to console (like tmux/screen)

# Chapter VII: Appendix

## VII.1 Example configuration file
```yaml
programs:
  nginx:
    cmd: "/usr/local/bin/nginx -c /etc/nginx/test.conf"
    numprocs: 1
    umask: 022
    workingdir: /tmp
    autostart: true
    autorestart: unexpected
    exitcodes: [0, 2]
    startretries: 3
    starttime: 5
    stopsignal: TERM
    stoptime: 10
    stdout: /tmp/nginx.stdout
    stderr: /tmp/nginx.stderr
    env:
      STARTED_BY: taskmaster
      ANSWER: 42
  vogsphere:
    cmd: "/usr/local/bin/vogsphere-worker --no-prefork"
    numprocs: 8
    umask: 077
    workingdir: /tmp
    autostart: true
    autorestart: unexpected
    exitcodes: [0]
    startretries: 3
    starttime: 5
    stopsignal: USR1
    stoptime: 10
    stdout: /tmp/vgsworker.stdout
    stderr: /tmp/vgsworker.stderr
```

## VII.2 Trying out `supervisor`
`supervisor` is available via PyPI. Create a virtualenv, `pip install supervisor`, then
`supervisord -c myconfigfile.conf` and interact via `supervisorctl`.

Reference supervisor for behavior patterns, but implement only the required features for this task.

# Chapter VIII: Submission and Peer Correction
Submit your work to your Git repository. Only repository content will be graded.

Good luck and don’t forget your AUTHORS file!
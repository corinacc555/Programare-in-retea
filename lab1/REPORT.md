# Laboratory Work 1: Concurrency in Practice

## 1. Hardware and software

| Item | Measured value |
|---|---|
| CPU model | |
| Physical cores | |
| Logical processors | |
| RAM | |
| Internal disk type | |
| USB drive and USB version | |
| Operating system | |
| Language and runtime version | |

Useful commands:

```powershell
dotnet --version
Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors
Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory
Get-Volume
```

## 2. Data and correctness

Input command:

```powershell
dotnet run --project .\lab1\lab1.csproj -c Release -- gen .\lab1\numbers.txt 50000000
```

Record the common negative, positive, zero, and total counts here:

| Negative | Positive | Zero | Total |
|---:|---:|---:|---:|
| | | | |

Every measured task must produce the same totals.

## 3. Task 1a

Record `time_ms` and `peak_memory_mb` for 1, 2, 4, 8, 16, 32, and 64
threads. Speed-up is `time(1) / time(n)`.

| Threads | Time (ms) | Speed-up | Peak memory (MB) | Counts correct? |
|---:|---:|---:|---:|---|
| 1 | | | | |
| 2 | | | | |
| 4 | | | | |
| 8 | | | | |
| 16 | | | | |
| 32 | | | | |
| 64 | | | | |

Answer questions 1-3 from the laboratory specification.

## 4. Task 1b

Record all three unsynchronised runs, then the locked and per-thread results.

| Version | Run | Time (ms) | Negative | Positive | Zero | Total | Correct? |
|---|---:|---:|---:|---:|---:|---:|---|
| Shared, unsynchronised | 1 | | | | | | |
| Shared, unsynchronised | 2 | | | | | | |
| Shared, unsynchronised | 3 | | | | | | |
| Shared, locked | 1 | | | | | | |
| Per-thread counters | 1 | | | | | | |

Answer question 4 from the laboratory specification.

## 5. Task 1c

Record `time_ms` and `peak_memory_mb` for 1, 2, 4, 8, 16, 32, and 64
workers. Compare these measurements with Task 1a.

| Workers | Time (ms) | Speed-up | Peak memory (MB) | Counts correct? |
|---:|---:|---:|---:|---|
| 1 | | | | |
| 2 | | | | |
| 4 | | | | |
| 8 | | | | |
| 16 | | | | |
| 32 | | | | |
| 64 | | | | |

Answer questions 5-6 from the laboratory specification.

## 6. Task 2

Record one run for 1, 2, and 4 threads while reading only from the USB drive.

| Threads | Time (ms) | Speed-up | Peak memory (MB) | Counts correct? |
|---:|---:|---:|---:|---|
| 1 | | | | |
| 2 | | | | |
| 4 | | | | |

Compare Task 2 with Task 1a using the measured values and answer both Task 2
questions from the specification.

## 7. Control questions

Answer all seven control questions from the laboratory specification.

## 8. Conclusion

State which measurements were CPU-bound and which were I/O-bound. Support the
classification with execution times, speed-up, memory, and the core counts.
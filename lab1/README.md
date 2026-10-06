# Network Programming Lab 1

## Build

From `D:\PR\lab1`:

```powershell
dotnet build -c Release
```

## Generate input

The generator is not part of the measured tasks:

```powershell
dotnet run -c Release -- gen numbers.txt 5000000
```

## Task 1a

Each thread reads and parses only its own byte range. Line boundaries are
handled so that every line is counted exactly once.

Run the required thread counts:

```powershell
1,2,4,8,16,32,64 | ForEach-Object {
    dotnet run -c Release -- 1a numbers.txt $_
}
```

The output contains the negative, positive, zero and total counts, elapsed
time in milliseconds, and peak working-set memory in megabytes.

## Task 1b

The numbers are loaded into an integer array before timing. The program then
uses `Environment.ProcessorCount` logical processors and prints:

* shared counters without synchronization, three times;
* shared counters protected by `lock`;
* one counter per thread, summed after joining all threads.

```powershell
dotnet run -c Release -- 1b numbers.txt
```

The unsynchronized version intentionally demonstrates a data race, so its
total can be lower than the number of input values.

## Task 1c

One producer reads lines into batches of 10,000 integers and adds them to a
bounded `BlockingCollection` containing at most 100 batches. Worker threads
consume batches until the producer completes the collection.

Run the required worker counts:

```powershell
1,2,4,8,16,32,64 | ForEach-Object {
    dotnet run -c Release -- 1c numbers.txt $_
}
```

The output contains the counts, elapsed time, and peak working-set memory.

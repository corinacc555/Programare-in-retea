using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

// Usage:
//   dotnet run -c Release -- gen numbers.txt 50000000
//   dotnet run -c Release -- split numbers.txt E:\task2
//   dotnet run -c Release -- 1a numbers.txt 1
//   dotnet run -c Release -- 1b numbers.txt
//   dotnet run -c Release -- 1c numbers.txt 4
//   dotnet run -c Release -- 2 E:\task2 1
internal static class Program
{
    private const int BufferSize = 1 << 20;
    private const int BatchSize = 10_000;
    private const int MaxQueuedBatches = 100;
    private const int Task2FileCount = 8;
    private const long NumbersPerTask2File = 6_250_000;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "gen":
                    RequireArgumentCount(args, 3);
                    Generate(args[1], long.Parse(args[2]));
                    return 0;
                case "split":
                    RequireArgumentCount(args, 3);
                    SplitForTask2(args[1], args[2]);
                    return 0;
                case "1a":
                    RequireArgumentCount(args, 3);
                    RunTask1a(args[1], int.Parse(args[2]));
                    return 0;
                case "1b":
                    RequireArgumentCount(args, 2);
                    RunTask1b(args[1]);
                    return 0;
                case "1c":
                    RequireArgumentCount(args, 3);
                    RunTask1c(args[1], int.Parse(args[2]));
                    return 0;
                case "2":
                    RequireArgumentCount(args, 3);
                    RunTask2(args[1], int.Parse(args[2]));
                    return 0;
                default:
                    Console.Error.WriteLine("Unknown mode: " + args[0]);
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex) when (ex is ArgumentException ||
                                   ex is FormatException ||
                                   ex is OverflowException ||
                                   ex is IOException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            if (ex.InnerException is not null)
                Console.Error.WriteLine("  " + ex.InnerException.Message);
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  gen <file> <count>");
        Console.Error.WriteLine("  split <file> <directory>");
        Console.Error.WriteLine("  1a <file> <threads>");
        Console.Error.WriteLine("  1b <file>");
        Console.Error.WriteLine("  1c <file> <workers>");
        Console.Error.WriteLine("  2 <directory> <workers>");
    }

    private static void RequireArgumentCount(string[] args, int count)
    {
        if (args.Length != count)
            throw new ArgumentException("Invalid number of arguments.");
    }

    private static void ValidateThreadCount(int count, string name)
    {
        if (count <= 0)
            throw new ArgumentException($"{name} must be greater than zero.");
    }

    // ---------- Data generation ----------
    private static void Generate(string path, long count)
    {
        if (count < 0)
            throw new ArgumentException("count must not be negative.");

        var rnd = new Random();
        using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false), BufferSize);
        writer.NewLine = "\n";
        for (long i = 0; i < count; i++)
            writer.WriteLine(rnd.Next(-1_000_000, 1_000_001));

        Console.WriteLine($"Wrote {count} numbers to {path}");
    }

    private static void SplitForTask2(string sourcePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var reader = new StreamReader(sourcePath);
        var writers = new StreamWriter[Task2FileCount];

        try
        {
            for (int i = 0; i < Task2FileCount; i++)
            {
                string path = Path.Combine(outputDirectory, $"numbers_{i + 1}.txt");
                writers[i] = new StreamWriter(path, false, new System.Text.UTF8Encoding(false), BufferSize)
                {
                    NewLine = "\n"
                };
            }

            long total = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (total >= Task2FileCount * NumbersPerTask2File)
                    throw new ArgumentException("Input contains more than 50000000 numbers.");

                int fileIndex = (int)(total / NumbersPerTask2File);
                writers[fileIndex].WriteLine(line);
                total++;
            }

            if (total != Task2FileCount * NumbersPerTask2File)
                throw new ArgumentException(
                    $"Input must contain exactly {Task2FileCount * NumbersPerTask2File} numbers; found {total}.");

            Console.WriteLine($"Wrote {Task2FileCount} files to {outputDirectory}");
        }
        finally
        {
            foreach (StreamWriter? writer in writers)
                writer?.Dispose();
        }
    }

    // ---------- Task 1a ----------
    private static void RunTask1a(string path, int threadCount)
    {
        ValidateThreadCount(threadCount, "threads");
        long fileLength = new FileInfo(path).Length;
        var results = new Counts[threadCount];
        var errors = new Exception?[threadCount];
        var threads = new Thread[threadCount];
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < threadCount; i++)
        {
            int id = i;
            long start = fileLength * id / threadCount;
            long end = fileLength * (id + 1) / threadCount;
            threads[i] = new Thread(() =>
            {
                try
                {
                    results[id] = CountRange(path, start, end);
                }
                catch (Exception ex)
                {
                    errors[id] = ex;
                }
            });
            threads[i].Start();
        }

        JoinAll(threads);
        stopwatch.Stop();

        foreach (Exception? error in errors)
        {
            if (error is not null)
                throw new IOException("Task 1a failed while reading the input file.", error);
        }

        Counts total = Add(results);
        PrintResult("1a", threadCount, total, stopwatch.ElapsedMilliseconds, true);
    }

    private static Counts CountRange(string path, long start, long end)
    {
        Counts counts = default;
        if (start >= end)
            return counts;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                          bufferSize: 1, FileOptions.SequentialScan);
        bool skipping = start > 0;
        long absolutePosition = start > 0 ? start - 1 : 0;
        stream.Seek(absolutePosition, SeekOrigin.Begin);

        var buffer = new byte[BufferSize];
        bool negative = false;
        bool inNumber = false;
        int value = 0;
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (skipping)
                {
                    if (b == (byte)'\n')
                    {
                        skipping = false;
                        if (absolutePosition + i + 1 >= end)
                            return counts;
                    }
                    continue;
                }

                if (b >= (byte)'0' && b <= (byte)'9')
                {
                    value = value * 10 + b - (byte)'0';
                    inNumber = true;
                }
                else if (b == (byte)'-' && !negative && !inNumber)
                {
                    negative = true;
                }
                else if (b == (byte)'\n' && inNumber)
                {
                    counts.Add(value, negative);
                    negative = false;
                    inNumber = false;
                    value = 0;
                    if (absolutePosition + i + 1 >= end)
                        return counts;
                }
                else if (b != (byte)'\r')
                {
                    throw InvalidInput(path, absolutePosition + i);
                }
            }
            absolutePosition += read;
        }

        if (!skipping && inNumber)
            counts.Add(value, negative);

        return counts;
    }

    // ---------- Task 1b ----------
    private static void RunTask1b(string path)
    {
        int[] numbers = LoadNumbers(path);
        int logicalProcessors = Environment.ProcessorCount;
        Console.WriteLine($"1b numbers={numbers.Length} logical_processors={logicalProcessors}");

        for (int run = 1; run <= 3; run++)
            RunSharedUnsynchronised(numbers, logicalProcessors, run);

        RunSharedLocked(numbers, logicalProcessors);
        RunPerThreadCounters(numbers, logicalProcessors);
    }

    private static int[] LoadNumbers(string path)
    {
        var numbers = new List<int>();
        using var reader = new StreamReader(path);
        string? line;
        while ((line = reader.ReadLine()) is not null)
            numbers.Add(int.Parse(line));
        return numbers.ToArray();
    }

    private static void RunSharedUnsynchronised(int[] numbers, int threadCount, int run)
    {
        int negative = 0, positive = 0, zero = 0;
        var threads = new Thread[threadCount];
        var stopwatch = Stopwatch.StartNew();
        StartIndexThreads(threadCount, numbers.Length, (id, start, end) =>
        {
            for (int i = start; i < end; i++)
            {
                if (numbers[i] < 0) negative++;
                else if (numbers[i] > 0) positive++;
                else zero++;
            }
        }, threads);
        JoinAll(threads);
        stopwatch.Stop();
        Console.WriteLine($"1b unsynchronised run={run} threads={threadCount} " +
                          $"negative={negative} positive={positive} zero={zero} " +
                          $"total={negative + positive + zero} time_ms={stopwatch.ElapsedMilliseconds}");
    }

    private static void RunSharedLocked(int[] numbers, int threadCount)
    {
        int negative = 0, positive = 0, zero = 0;
        object sync = new object();
        var threads = new Thread[threadCount];
        var stopwatch = Stopwatch.StartNew();
        StartIndexThreads(threadCount, numbers.Length, (id, start, end) =>
        {
            for (int i = start; i < end; i++)
            {
                lock (sync)
                {
                    if (numbers[i] < 0) negative++;
                    else if (numbers[i] > 0) positive++;
                    else zero++;
                }
            }
        }, threads);
        JoinAll(threads);
        stopwatch.Stop();
        Console.WriteLine($"1b locked threads={threadCount} negative={negative} positive={positive} " +
                          $"zero={zero} total={negative + positive + zero} time_ms={stopwatch.ElapsedMilliseconds}");
    }

    private static void RunPerThreadCounters(int[] numbers, int threadCount)
    {
        var results = new Counts[threadCount];
        var threads = new Thread[threadCount];
        var stopwatch = Stopwatch.StartNew();
        StartIndexThreads(threadCount, numbers.Length, (id, start, end) =>
        {
            Counts counts = default;
            for (int i = start; i < end; i++)
                counts.Add(numbers[i], numbers[i] < 0);
            results[id] = counts;
        }, threads);
        JoinAll(threads);
        stopwatch.Stop();
        Counts total = Add(results);
        Console.WriteLine($"1b per-thread threads={threadCount} {total} " +
                          $"total={total.Total} time_ms={stopwatch.ElapsedMilliseconds}");
    }

    private static void StartIndexThreads(int threadCount, int length,
                                          Action<int, int, int> action, Thread[] threads)
    {
        for (int i = 0; i < threadCount; i++)
        {
            int id = i;
            int start = length * id / threadCount;
            int end = length * (id + 1) / threadCount;
            threads[i] = new Thread(() => action(id, start, end));
            threads[i].Start();
        }
    }

    // ---------- Task 1c ----------
    private static void RunTask1c(string path, int workerCount)
    {
        ValidateThreadCount(workerCount, "workers");
        var queue = new BlockingCollection<List<string>>(MaxQueuedBatches);
        var results = new Counts[workerCount];
        var workerErrors = new Exception?[workerCount];
        Exception? producerError = null;
        var stopwatch = Stopwatch.StartNew();

        var producer = new Thread(() =>
        {
            try
            {
                using var reader = new StreamReader(path);
                var batch = new List<string>(BatchSize);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    batch.Add(line);
                    if (batch.Count == BatchSize)
                    {
                        queue.Add(batch);
                        batch = new List<string>(BatchSize);
                    }
                }
                if (batch.Count > 0)
                    queue.Add(batch);
            }
            catch (Exception ex)
            {
                producerError = ex;
            }
            finally
            {
                queue.CompleteAdding();
            }
        });

        var workers = new Thread[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            int id = i;
            workers[i] = new Thread(() =>
            {
                Counts counts = default;
                // A failed worker keeps draining the queue so the reader never blocks forever.
                foreach (List<string> batch in queue.GetConsumingEnumerable())
                {
                    if (workerErrors[id] is not null)
                        continue;

                    try
                    {
                        foreach (string line in batch)
                        {
                            int number = int.Parse(line);
                            counts.Add(number, number < 0);
                        }
                    }
                    catch (Exception ex)
                    {
                        workerErrors[id] = ex;
                    }
                }
                results[id] = counts;
            });
        }

        producer.Start();
        foreach (Thread worker in workers)
            worker.Start();
        producer.Join();
        JoinAll(workers);
        stopwatch.Stop();
        queue.Dispose();

        if (producerError is not null)
            throw new IOException("Producer failed while reading the input file.", producerError);

        foreach (Exception? error in workerErrors)
        {
            if (error is not null)
                throw new FormatException("Worker failed while parsing the input file.", error);
        }

        Counts total = Add(results);
        PrintResult("1c", workerCount, total, stopwatch.ElapsedMilliseconds, true);
    }

    // ---------- Task 2 ----------
    private static void RunTask2(string directory, int workerCount)
    {
        if (workerCount is not (1 or 2 or 4))
            throw new ArgumentException("Task 2 workers must be 1, 2, or 4.");

        var paths = new string[Task2FileCount];
        for (int i = 0; i < paths.Length; i++)
        {
            paths[i] = Path.Combine(directory, $"numbers_{i + 1}.txt");
            if (!File.Exists(paths[i]))
                throw new FileNotFoundException("Missing Task 2 input file.", paths[i]);
        }

        var results = new Counts[workerCount];
        var errors = new Exception?[workerCount];
        var threads = new Thread[workerCount];
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < workerCount; i++)
        {
            int id = i;
            int start = paths.Length * id / workerCount;
            int end = paths.Length * (id + 1) / workerCount;
            threads[i] = new Thread(() =>
            {
                try
                {
                    Counts counts = default;
                    for (int fileIndex = start; fileIndex < end; fileIndex++)
                        counts += CountWholeFile(paths[fileIndex]);
                    results[id] = counts;
                }
                catch (Exception ex)
                {
                    errors[id] = ex;
                }
            });
            threads[i].Start();
        }

        JoinAll(threads);
        stopwatch.Stop();

        foreach (Exception? error in errors)
        {
            if (error is not null)
                throw new IOException("Task 2 failed while reading the input files.", error);
        }

        Counts total = Add(results);
        PrintResult("2", workerCount, total, stopwatch.ElapsedMilliseconds, true);
    }

    private static Counts CountWholeFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Counts counts = default;
        bool negative = false;
        bool inNumber = false;
        int value = 0;

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                value = value * 10 + b - (byte)'0';
                inNumber = true;
            }
            else if (b == (byte)'-' && !negative && !inNumber)
            {
                negative = true;
            }
            else if (b == (byte)'\n' && inNumber)
            {
                counts.Add(value, negative);
                negative = false;
                inNumber = false;
                value = 0;
            }
            else if (b != (byte)'\r')
            {
                throw InvalidInput(path, i);
            }
        }

        if (inNumber)
            counts.Add(value, negative);

        return counts;
    }

    private static FormatException InvalidInput(string path, long position) =>
        new FormatException($"Invalid input in {path} at byte {position}.");

    private static void JoinAll(Thread[] threads)
    {
        foreach (Thread thread in threads)
            thread.Join();
    }

    private static Counts Add(Counts[] values)
    {
        Counts total = default;
        foreach (Counts value in values)
        {
            total.Negative += value.Negative;
            total.Positive += value.Positive;
            total.Zero += value.Zero;
        }
        return total;
    }

    private static void PrintResult(string task, int workers, Counts counts,
                                    long elapsedMilliseconds, bool includeMemory)
    {
        Console.WriteLine($"{task} workers={workers} {counts} " +
                          $"total={counts.Total} time_ms={elapsedMilliseconds}" +
                          (includeMemory
                              ? " peak_memory_mb=" +
                                (Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0))
                                    .ToString("F1", CultureInfo.InvariantCulture)
                              : string.Empty));
    }

    private struct Counts
    {
        public long Negative;
        public long Positive;
        public long Zero;

        public long Total => Negative + Positive + Zero;

        public static Counts operator +(Counts left, Counts right) =>
            new Counts
            {
                Negative = left.Negative + right.Negative,
                Positive = left.Positive + right.Positive,
                Zero = left.Zero + right.Zero
            };

        public void Add(int value, bool negative)
        {
            if (value == 0) Zero++;
            else if (negative) Negative++;
            else Positive++;
        }

        public override readonly string ToString() =>
            $"negative={Negative} positive={Positive} zero={Zero}";
    }
}

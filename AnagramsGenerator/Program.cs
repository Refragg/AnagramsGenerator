using System.Globalization;

namespace AnagramsGenerator;

public class Program
{
    private static string _input;
    private static string _inputNoDuplicates;
    private static int _tasksNumber;
    
    private static long _permutationsTotal;
    private static long _permutationsPerTask;
    private static long _anagramsTotal;
    
    private static bool _cancel;
    private static bool _queueNotEmptyMessagePrinted;
    private static bool _endReached;

    private static Queue<string> _queue = new();
    private static long _anagramsFound;
    private static Task[] _tasks;
    
    private const string OutputFileName = "output.txt";
    private const string WorkerStateFileName = "worker-state-";
    private const string WorkerStateFileExtension = ".txt";
    
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("No input word provided");
            return;
        }
        
        _input = args[0];
        _inputNoDuplicates = new string(args[0].Distinct().ToArray());
        _tasksNumber = _inputNoDuplicates.Length;
        _tasks = new Task[_tasksNumber];
        
        _permutationsTotal = NumberOfPermutations(_input);
        _permutationsPerTask = _permutationsTotal / _inputNoDuplicates.Length;
        _anagramsTotal = NumberOfAnagrams(_input);
        
        Console.CancelKeyPress += (o, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Canceling...");
            _cancel = true;
        };

        Console.WriteLine($"Searching for all {_anagramsTotal} possible anagrams in '{_input}'...");

        bool resume = false;
        
        if (args.Length > 1 && (args[1] == "-r" || args[1] == "--resume"))
        {
            resume = true;
            _anagramsFound = GetAnagramsFoundCount();
            Console.WriteLine($"Resuming at {_anagramsFound} anagrams...");
        }

        Console.WriteLine($"Starting {_tasksNumber} workers...");
        
        for (byte i = 0; i < _tasksNumber; i++)
        {
            byte indexStart = i;
            _tasks[i] = new Task(() => TaskDoWork(indexStart, resume));
            _tasks[i].Start();
        }
        
        // Start a thread to wait for our worker tasks and then signal that the end was reached
        new Thread(() =>
        {
            Task.WaitAll(_tasks);
            _endReached = true;
        }).Start();
        
        using StreamWriter fs = new StreamWriter(File.Open(OutputFileName, FileMode.Append, FileAccess.Write));
        
        string candidate;
        bool gotString;
        bool taskExited = false;
        
        while (true)
        {
            if (_endReached || _cancel)
            {
                // Wait for the end of the tasks when cancelling to avoid missing potential queue elements
                if (_cancel)
                    WaitForTasks();
                
                if (_queue.Count > 0)
                {
                    if (!_queueNotEmptyMessagePrinted)
                    {
                        Console.WriteLine("Queue is not empty, processing remaining data before stopping...");
                        _queueNotEmptyMessagePrinted = true;
                    }
                }
                else
                {
                    fs.Close();
                    break;
                }
            }
            
            lock (_queue) 
                gotString = _queue.TryDequeue(out candidate!);

            if (!gotString)
            {
                Thread.Sleep(1);
                continue;
            }
            
            fs.WriteLine(candidate);
            _anagramsFound++;
        }

        if (_cancel)
        {
            Console.WriteLine("Canceled successfully");
            double anagramsFoundPercent = 100.0 * _anagramsFound / _anagramsTotal;
            Console.WriteLine($"Current progress: {_anagramsFound} / {_anagramsTotal} ({anagramsFoundPercent.ToString("F2", CultureInfo.InvariantCulture)}%) anagrams found");
            return;
        }

        WaitForTasks();
        Console.WriteLine("Done!");
    }
    
    static void TaskDoWork(byte taskNumber, bool resume)
    {
        bool taskEndReached = false;
        byte[] columns = new byte[_input.Length];
        Span<char> stringBuffer = stackalloc char[_input.Length];
        long count = 0;

        columns[0] = taskNumber;

        if (resume)
        {
            (count, columns) = GetTaskInfo(taskNumber);
            Console.WriteLine($"Worker {taskNumber + 1}: resuming at {count}");
        }
        
        while (!_cancel && !taskEndReached)
        {
            count++;

            // Report progress to the user every 5 000 000 000 permutations tested
            if (count % 5_000_000_000 == 0)
            {
                double permutationsTestedPercent = 100.0 * count / _permutationsPerTask;
                Console.WriteLine($"Worker {taskNumber + 1}: {count} / {_permutationsPerTask} ({permutationsTestedPercent.ToString("F2", CultureInfo.InvariantCulture)}%)");
            }
            
            for (int j = 0; j < columns.Length; j++)
                stringBuffer[j] = _inputNoDuplicates[columns[j]];

            if (Valid(stringBuffer))
                lock (_queue) 
                    _queue.Enqueue(stringBuffer.ToString());
            
            // Increment column!
            for (int i = columns.Length - 1;; i--)
            {
                if (columns[i] + 1 == _inputNoDuplicates.Length)
                {
                    if (i == 1)
                    {
                        taskEndReached = true;
                        break;
                    }
                    
                    columns[i] = 0;
                    continue;
                }

                columns[i]++;
                break;
            }
        }

        if (taskEndReached)
        {
            Console.WriteLine($"Worker {taskNumber + 1}: done");
            return;
        }

        // The user canceled the processing, save the current progress and end the task
        SaveTaskInfo(taskNumber, count, columns);
        Console.WriteLine($"Saved worker {taskNumber + 1} at {count}");
    }
    
    // An anagram will only be valid if it contains the same characters counts as the input
    static bool Valid(Span<char> output)
    {
        for (var i = 0; i < _input.Length; i++)
            if (CountCharactersInInput(_input[i]) != CountCharacters(output, _input[i]))
                return false;

        return true;
    }
    
    static int CountCharacters(Span<char> input, char c)
    {
        int count = 0;
        
        for (int i = 0; i < input.Length; i++)
            if (input[i] == c)
                count++;

        return count;
    }
    
    // Optimized to avoid passing the input string everytime
    static int CountCharactersInInput(char c)
    {
        int count = 0;
        
        for (int i = 0; i < _input.Length; i++)
            if (_input[i] == c)
                count++;

        return count;
    }

    // Get the total number of anagrams for a given input
    // x! / (c1! * c2! * c3! * ...)
    // Where x is the input string and c1, c2, ... are all the input string characters counts  
    static long NumberOfAnagrams(string input)
    {
        // Very lazy and inefficient but working way to get the count of all the duplicated letters in the input string
        Dictionary<char, long> duplicateLetters = input
            .Select(c => new KeyValuePair<char, long>(c, CountCharactersInInput(c))) // Map character to it's count in the input string
            .DistinctBy(x => x.Key) // Discard duplicate character count entries
            .Where(p => p.Value > 1) // Only take characters that occur more than once
            .ToDictionary();
        
        long duplicateLettersFactorials = 1;
        foreach (int duplicateLetterCount in duplicateLetters.Values)
            duplicateLettersFactorials *= Factorial(duplicateLetterCount);
        
        return Factorial(input.Length) / duplicateLettersFactorials;
    }
    
    // x ^ y where x is the amount of different possibilities and y is the length of the output
    static long NumberOfPermutations(string input) => (long)Math.Pow(input.Distinct().Count(), input.Length);
    
    // Performs x!
    static long Factorial(int number)
    {
        long sum = 1;

        for (long i = 2; i <= number; i++)
            sum *= i;

        return sum;
    }
    
    static long GetAnagramsFoundCount()
    {
        return File.ReadAllLines(OutputFileName).LongCount();
    }

    static void SaveTaskInfo(byte taskNumber, long count, byte[] columns)
    {
        List<string> lines = new List<string>();
        lines.Add(count.ToString());
        lines.AddRange(columns.Select(x => x.ToString()));
        
        File.WriteAllLines(WorkerStateFileName + taskNumber + WorkerStateFileExtension, lines);
    }

    static (long, byte[]) GetTaskInfo(byte taskNumber)
    {
        string[] lines = File.ReadAllLines(WorkerStateFileName + taskNumber + WorkerStateFileExtension);
        
        long count = long.Parse(lines[0]);
        byte[] columns = lines[1..].Select(byte.Parse).ToArray();
        return (count, columns);
    }
    
    static void WaitForTasks()
    {
        while (!_endReached)
            Thread.Sleep(10);
    }
}
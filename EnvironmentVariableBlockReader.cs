using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;


// This software is my own, very "to the point", and WORKING implementation of how to read any Windows process environment variable block.
// It is meant to be something useful, but not something mind-blowing or definitive. Adapt or remake the code to meet your use case needs.

// I think this doesn't need to be said, but just in case:  Microsoft / Windows API does NOT provide you any direct means to achieve this!
// As you can see in this code, it requires to follow a series of very specific, REALLY ERROR-PRONE steps, in order to be able to succeed.


class EnvironmentVariableBlockReader
{

    [DllImport("ntdll.dll", SetLastError = true, ExactSpelling = true)]
    static extern bool NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, uint processInformationLength, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, uint dwSize, out IntPtr lpNumberOfBytesRead);

    // We only care about the memory address pointer to PEB, but truncating PBI will result in error.
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION
    {
        public int ExitStatus;
        public IntPtr PebBaseAddress;
        public UIntPtr AffinityMask;
        public int BasePriority;
        public UIntPtr UniqueProcessId;
        public UIntPtr InheritedFromUniqueProcessId;
    }

    // We only care about the memory address pointer to ProcessParameters, so we don't read beyond that point.
    [StructLayout(LayoutKind.Sequential)]
    struct PEB_TRUNCATED
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] Reserved1;
        public byte BeingDebugged;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] Reserved2;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public IntPtr[] Reserved3;
        public IntPtr Ldr;
        public IntPtr ProcessParameters;
    }

    // We only care about the memory address pointer to Environment and the EnvironmentSize, so we don't read beyond that point.
    // PLEASE KEEP IN MIND THAT X64 REFERS TO THE TARGET PROCESS ARCHITECTURE, NOT TO THE CURRENT WINDOWS OS ARCHITECTURE!!
    [StructLayout(LayoutKind.Sequential)]
    struct RTL_USER_PROCESS_PARAMETERS_x64_TRUNCATED
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x80)]
        public byte[] Offset1;
        public IntPtr Environment;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x368)]
        public byte[] Offset2;
        public uint EnvironmentSize;
    }

    // We only care about the memory address pointer to Environment and the EnvironmentSize, so we don't read beyond that point.
    // PLEASE KEEP IN MIND THAT X86 REFERS TO THE TARGET PROCESS ARCHITECTURE, NOT TO THE CURRENT WINDOWS OS ARCHITECTURE!!
    [StructLayout(LayoutKind.Sequential)]
    struct RTL_USER_PROCESS_PARAMETERS_x86_TRUNCATED
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x48)]
        public byte[] Offset1;
        public IntPtr Environment;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x244)]
        public byte[] Offset2;
        public uint EnvironmentSize;
    }

    const int ProcessBasicInformation = 0;

    static void Main(string[] args)
    {

        int position = 0;
        int selectedProcessID = 0;
        IntPtr processParameters = IntPtr.Zero;
        string otherArgumentValue = null;
        string[] split = null;
        string[] environmentStrings = null;
        byte[] environmentData = null;
        
        char[] driveLetters = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };
        
        string[] reservedNames = { "AUX", "CON", "NUL", "PRN",
                            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        
        // My own personal Windows File/Folder/Path validator. Rejects UNC paths, otherwise strictly sticks by Windows' validation rules.
        bool IsValidFileFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            foreach (char c in Path.GetInvalidPathChars())
            {
                if (path.Contains(c))
                {
                    return false;
                }
            }
            path = path.Replace("/", @"\");
            if (path.Length == 0 || path.Length > 260 || path.StartsWith(" ") || path.StartsWith(":") ||
                path.StartsWith(@"\") || path.EndsWith(" ") || path.EndsWith(".") || path.Contains(@"\\"))
            {
                return false;
            }
            if (path.Length > 1)
            {
                if (path[1] == ':')
                {
                    if (!driveLetters.Contains(path.ToUpper()[0]))
                    {
                        return false;
                    }
                    if (path.Length > 2)
                    {
                        if (path[2] != '\\')
                        {
                            return false;
                        }
                        string substring = path.Substring(2);
                        if (substring.Contains(':'))
                        {
                            return false;
                        }
                    }
                    position = 1;
                }
                else
                {
                    if (path.Contains(':'))
                    {
                        return false;
                    }
                    position = 0;
                }
                if (path.Length > 2)
                {
                    split = path.Split('\\');
                    for (int i = position; i < split.Length; i++)
                    {
                        if (split[i] == "..")
                        {
                            continue;
                        }
                        if (reservedNames.Contains(split[i].Split('.')[0].ToUpper()) || split[i].StartsWith(" ") || split[i].EndsWith(" ") || split[i].EndsWith("."))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        // EVBR's own internal argument validator.
        bool IsValidArgument(string argument)
        {
            if (!argument.ToLower().StartsWith("pid=") && !argument.ToLower().StartsWith("crlf=") && !argument.ToLower().StartsWith("null=") && !argument.ToLower().StartsWith("path="))
            {
                return false;
            }
            split = argument.Split('=');
            if (split.Length != 2)
            {
                return false;
            }
            if (split[0].ToLower() == "pid")
            {
                return int.TryParse(split[1], NumberStyles.None, null, out int _);
            }
            if (split[0].ToLower() == "path" && split[1] == ".")
            {
                return true;
            }
            return IsValidFileFolderPath(split[1]);
        }

        // EVBR's own internal first subroutine. Called by RunSecondSubroutine() and by RoutineGetStrings()
        void RunFirstSubroutine()
        {
            if (environmentData == null || environmentData.Length == 0)
            {
                Console.Error.WriteLine($"UNEXPECTED ERROR (-8)");
                Environment.Exit(-8);
            }
            environmentStrings = EnvironmentBlockToArray(environmentData);
            if (environmentStrings == null || environmentStrings.Length == 0)
            {
                Console.Error.WriteLine($"UNEXPECTED ERROR (-9)");
                Environment.Exit(-9);
            }
        }

        // EVBR's own internal second subroutine. Called by RoutineGetStrings()
        void RunSecondSubroutine()
        {
            if (Environment.Is64BitOperatingSystem)
            {

                // The target process is x64.
                
                if (Environment.Is64BitProcess)
                {
                    environmentData = GetX64ProcessEnvironment(selectedProcessID, processParameters);
                    RunFirstSubroutine();
                }
                else
                {

                    // x86 processes can't read x64 processes.
                    
                    Console.Error.WriteLine($"EVBR.exe is running as a 32 bit process, but the target is a 64 bit process. By Windows' design, this won't work.");
                    Environment.Exit(-9);
                }
            }
            else
            {
                Console.Error.WriteLine($"UNEXPECTED ERROR (-9)");
                Environment.Exit(-9);
            }
        }

        // EVBR's own internal routine to obtain a string[] with selectedProcessID's environment block, or an error.
        void RoutineGetStrings()
        {
            processParameters = GetProcessParameters(selectedProcessID);
            if (processParameters == IntPtr.Zero)
            {
                Console.Error.WriteLine($"UNEXPECTED ERROR (-7)");
                Environment.Exit(-7);
            }
            if (selectedProcessID == Process.GetCurrentProcess().Id)
            {
                if (Environment.Is64BitProcess)
                {
                    environmentData = GetX64ProcessEnvironment(selectedProcessID, processParameters);
                }
                else
                {
                    environmentData = GetX86ProcessEnvironment(selectedProcessID, processParameters);
                }
                RunFirstSubroutine();
            }
            else
            {

                // We don't know if the target process if x86 or x64, so we try x86 first.

                environmentData = GetX86ProcessEnvironment(selectedProcessID, processParameters);
                if (environmentData == null || environmentData.Length == 0)
                {
                    RunSecondSubroutine();
                }
                else
                {
                    environmentStrings = EnvironmentBlockToArray(environmentData);
                    if (environmentStrings == null || environmentStrings.Length == 0)
                    {
                        RunSecondSubroutine();
                    }

                    // The target process is x86.

                }
            }
        }

        // EVBR's own internal routine to make otherArgumentValue an absolute path and simplify it by manually processing backwards traversing.
        void ProcessFileFolderArgument()
        {
            otherArgumentValue = otherArgumentValue.Replace("/", @"\");
            if (!Path.IsPathRooted(otherArgumentValue))
            {
                otherArgumentValue = Path.Combine(Environment.CurrentDirectory, otherArgumentValue);
            }
            while (true)
            {
                split = otherArgumentValue.Split('\\');
                if (split.Length < 3)
                {
                    break;
                }
                position = 0;
                for (int i = 2; i < split.Length; i++)
                {
                    if (split[i] == "..")
                    {
                        position = i;
                        break;
                    }
                }
                if (position == 0)
                {
                    break;
                }
                int previous = position - 1;
                string newArgumentValue = string.Empty;
                for (int i = 0; i < split.Length; i++)
                {
                    if (i == previous || i == position)
                    {
                        continue;
                    }
                    newArgumentValue = $"{newArgumentValue}\\{split[i]}";
                }
                otherArgumentValue = newArgumentValue.Trim('\\');
            }
        }

        // EVBR's execution starts here.
        if (args.Length == 0)
        {
            string[] usageInformation ={
                string.Empty,
                "Environment Variable Block Reader v1.0.1.0 by fireYtail                                  Usage (case insensitive)",
                string.Empty,
                "evbr.exe pid=0                                Writes all variables of EVBR itself to console / standard output.",
                "evbr.exe pid=0 crlf=[FILE]                    Writes all variables of EVBR itself to [FILE] in UTF-8  (CR - LF)",
                "evbr.exe pid=0 null=[FILE]                    Writes all variables of EVBR itself to [FILE] in UTF-16 (NULL NULL)",
                "evbr.exe pid=0 path=[FOLDER]                  Writes all variables of EVBR itself to [FOLDER] (UTF-16  files)",
                "evbr.exe pid=[PROCESS]                        Writes all variables of a [PROCESS] to console / standard output.",
                "evbr.exe pid=[PROCESS] crlf=[FILE]            Writes all variables of a [PROCESS] to [FILE] in UTF-8  (CR - LF)",
                "evbr.exe pid=[PROCESS] null=[FILE]            Writes all variables of a [PROCESS] to [FILE] in UTF-16 (NULL NULL)",
                "evbr.exe pid=[PROCESS] path=[FOLDER]          Writes all variables of a [PROCESS] to [FOLDER] (UTF-16  files)",
                string.Empty,
                "[PROCESS]                                     0 to target EVBR, otherwise a valid, currently running process ID.",
                "                                              You must have permission to read the specified process' memory.",
                "                                              Elevation is pretty much a requirement for sucess unless using 0.",
                string.Empty,
                "[FILE]                                        A file name, with or without an absolute or a relative path.",
                "                                              Note that if no extension is included, then no extension is used.",
                "                                              Backwards traversing is supported, final slash is required (..\\)",
                string.Empty,
                "[FOLDER]                                      A folder in the current directory, or an absolute or relative path.",
                "                                              The file names match the variable names. Only the values are saved.",
                "                                              If you want to use the current directory, use only a period (path=.)",
                string.Empty,
                "Exit code 0 means no arguments or sucess. Codes -1 to -6 are argument errors. -7 to -13 mean common exceptions or",
                "an unexpected error. Exit codes 1 and higher match \"universal\" Win32Exceptions, search the error messages online.",
                "This software is free and open sourced  (.NET Framework v4.6, C# )   If you paid for it, you have been scammed..." };
            foreach (string line in usageInformation)
            {
                Console.WriteLine(line);
            }
            Environment.Exit(0);
        }
        if (args.Length > 2)
        {
            Console.Error.WriteLine("More than two (2) arguments were passed. Make sure to use quotes (\") properly. To get help, just type evbr.exe");
            Environment.Exit(-3);
        }
        if (!IsValidArgument(args[0]))
        {
            Console.Error.WriteLine($"The first (1st) argument is invalid: \"{args[0]}\". To get help, just type evbr.exe");
            Environment.Exit(-1);
        }
        if (args.Length == 1 && !args[0].ToLower().StartsWith("pid="))
        {
            Console.Error.WriteLine($"The required argument pid=[PROCESS] wasn't passed. To get help, just type evbr.exe");
            Environment.Exit(-4);
        }
        if (args.Length == 2)
        {
            if (!IsValidArgument(args[1]))
            {
                Console.Error.WriteLine($"The second (2nd) argument is invalid: \"{args[1]}\". To get help, just type evbr.exe");
                Environment.Exit(-2);
            }
            string[] starts = { "pid=", "crlf=", "null=", "path=" };
            foreach (string s in starts)
            {
                if (args[0].ToLower().StartsWith(s) && args[1].ToLower().StartsWith(s))
                {
                    Console.Error.WriteLine($"The first and the second arguments are the same. To get help, just type evbr.exe");
                    Environment.Exit(-5);
                }
            }
            if (!args[0].ToLower().StartsWith("pid=") && !args[1].ToLower().StartsWith("pid="))
            {
                Console.Error.WriteLine($"The required argument pid=[PROCESS] wasn't passed. To get help, just type evbr.exe");
                Environment.Exit(-4);
            }
        }
        if (args[0].ToLower().StartsWith("pid="))
        {
            selectedProcessID = int.Parse(args[0].Split('=')[1]);
        }
        else
        {
            selectedProcessID = int.Parse(args[1].Split('=')[1]);
        }
        if (selectedProcessID == 0)
        {
            selectedProcessID = Process.GetCurrentProcess().Id;
        }
        else
        {
            try
            {
                string testString = Process.GetProcessById(selectedProcessID).ProcessName;
            }
            catch (Exception)
            {
                Console.Error.WriteLine($"The specified process (ID: {selectedProcessID}) doesn't exist. To get help, just type evbr.exe");
                Environment.Exit(-6);
            }
            try
            {
                Process.EnterDebugMode();
            }
            catch (Exception)
            {
                Console.WriteLine("WARNING! You are not running as administrator or don't have SeDebugPrivilege. This will make everything much more likely to fail...");
            }

            // No more internal error checks from this point onwards.

        }
        if (args.Length == 1)
        {
            RoutineGetStrings();
            foreach (string line in environmentStrings)
            {
                Console.WriteLine(line);
            }
            Environment.Exit(0);
        }
        if (!args[0].ToLower().StartsWith("pid="))
        {
            split = args[0].Split('=');
        }
        else
        {
            split = args[1].Split('=');
        }
        string otherArgumentType = split[0].ToLower();
        otherArgumentValue = split[1];
        if (otherArgumentType == "crlf")
        {
            RoutineGetStrings();
            ProcessFileFolderArgument();
            try
            {
                File.WriteAllLines(otherArgumentValue, environmentStrings, Encoding.UTF8);
                Console.WriteLine($"SUCCESS for process ID {selectedProcessID} and UTF-8 file {otherArgumentValue}");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MiniExceptionHandler(ex);
                Environment.Exit(-10);
            }
        }
        if (otherArgumentType == "null")
        {
            processParameters = GetProcessParameters(selectedProcessID);
            if (processParameters == IntPtr.Zero)
            {
                Console.Error.WriteLine($"UNEXPECTED ERROR (-7)");
                Environment.Exit(-7);
            }
            if (selectedProcessID == Process.GetCurrentProcess().Id)
            {
                if (Environment.Is64BitProcess)
                {
                    environmentData = GetX64ProcessEnvironment(selectedProcessID, processParameters);
                }
                else
                {
                    environmentData = GetX86ProcessEnvironment(selectedProcessID, processParameters);
                }
                if (environmentData == null || environmentData.Length == 0)
                {
                    Console.Error.WriteLine($"UNEXPECTED ERROR (-8)");
                    Environment.Exit(-8);
                }
            }
            else
            {
                environmentData = GetX86ProcessEnvironment(selectedProcessID, processParameters);
                if (environmentData == null || environmentData.Length == 0)
                {
                    environmentData = GetX64ProcessEnvironment(selectedProcessID, processParameters);
                    if (environmentData == null || environmentData.Length == 0)
                    {
                        Console.Error.WriteLine($"UNEXPECTED ERROR (-8)");
                        Environment.Exit(-8);
                    }
                }
            }
            ProcessFileFolderArgument();
            try
            {
                File.WriteAllBytes(otherArgumentValue, environmentData);
                Console.WriteLine($"SUCCESS for process ID {selectedProcessID} and UTF-16 file {otherArgumentValue}");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MiniExceptionHandler(ex);
                Environment.Exit(-11);
            }
        }
        if (otherArgumentType == "path")
        {
            RoutineGetStrings();
            Dictionary<string, string> environmentDict = EnvironmentArrayToDict(environmentStrings);
            if (otherArgumentValue == ".")
            {
                otherArgumentValue = Environment.CurrentDirectory;
            }
            else
            {
                ProcessFileFolderArgument();
            }
            bool error = false;
            foreach (KeyValuePair<string, string> variable in environmentDict)
            {
                try
                {
                    File.WriteAllText(Path.Combine(otherArgumentValue, variable.Key), variable.Value, Encoding.Unicode);
                }
                catch (Exception ex)
                {
                    MiniExceptionHandler(ex);
                    error = true;
                }
            }
            if (error)
            {
                Environment.Exit(-12);
            }
            Console.WriteLine($"SUCCESS for process ID {selectedProcessID} and UTF-16 files in path {otherArgumentValue}");
            Environment.Exit(0);
        }

        // This will never happen with my unmodified code, but it's here for you.

        Console.Error.WriteLine("ALL ARGUMENTS INVALID OR SOME OVERSIGHT...");
        Environment.Exit(-13);
    }

    // Returns a memory address pointer to a process' UPP struct, or exits with an error.
    static IntPtr GetProcessParameters(int processID)
    {
        try
        {
            IntPtr hProcess = Process.GetProcessById(processID).Handle;
            PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(hProcess, ProcessBasicInformation, ref pbi, (uint)Marshal.SizeOf(pbi), out _))
            {
                int lastWin32Error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"NtQueryInformationProcess ERROR ({lastWin32Error})");
                Environment.Exit(lastWin32Error);
            }
            PEB_TRUNCATED peb = new PEB_TRUNCATED();
            byte[] pebBuffer = new byte[Marshal.SizeOf(peb)];
            if (!ReadProcessMemory(hProcess, pbi.PebBaseAddress, pebBuffer, (uint)pebBuffer.Length, out _))
            {
                int lastWin32Error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"ReadProcessMemory [PBI] ERROR ({lastWin32Error})");
                Environment.Exit(lastWin32Error);
            }
            peb = ByteArrayToStructure<PEB_TRUNCATED>(pebBuffer);
            return peb.ProcessParameters;
        }
        catch (Exception ex)
        {
            MiniExceptionHandler(ex);
            Environment.Exit(-7);
            return IntPtr.Zero;
        }
    }

    // Returns the environment block from a 32 bit process, or exits with an error.
    // If the process' architecture is wrong, may return an invalid byte[] rather than exit.
    static byte[] GetX86ProcessEnvironment(int processID, IntPtr processParameters)
    {
        try
        {
            IntPtr hProcess = Process.GetProcessById(processID).Handle;
            RTL_USER_PROCESS_PARAMETERS_x86_TRUNCATED upp = new RTL_USER_PROCESS_PARAMETERS_x86_TRUNCATED();
            byte[] uppBuffer = new byte[Marshal.SizeOf(upp)];
            if (!ReadProcessMemory(hProcess, processParameters, uppBuffer, (uint)uppBuffer.Length, out _))
            {
                int lastWin32Error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"ReadProcessMemory [UPPx86] ERROR ({lastWin32Error})");
                Environment.Exit(lastWin32Error);
            }
            upp = ByteArrayToStructure<RTL_USER_PROCESS_PARAMETERS_x86_TRUNCATED>(uppBuffer);
            byte[] environmentBuffer = new byte[upp.EnvironmentSize];
            if (!ReadProcessMemory(hProcess, upp.Environment, environmentBuffer, (uint)environmentBuffer.Length, out _))
            {
                int lastWin32Error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"ReadProcessMemory [ENVx86] ERROR ({lastWin32Error})");
                Environment.Exit(lastWin32Error);
            }
            return environmentBuffer;
        }
        catch (Exception ex)
        {
            MiniExceptionHandler(ex);
            Environment.Exit(-8);
            return null;
        }
    }

    // Returns the environment block from a 64 bit process, or exits with an error.
    // If the process' architecture is wrong, may return an invalid byte[] rather than exit.
    static byte[] GetX64ProcessEnvironment(int processID, IntPtr processParameters)
    {
        try
        {
            IntPtr hProcess = Process.GetProcessById(processID).Handle;
            RTL_USER_PROCESS_PARAMETERS_x64_TRUNCATED upp = new RTL_USER_PROCESS_PARAMETERS_x64_TRUNCATED();
            byte[] uppBuffer = new byte[Marshal.SizeOf(upp)];
            if (!ReadProcessMemory(hProcess, processParameters, uppBuffer, (uint)uppBuffer.Length, out _))
            {
                int lastWin32Error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"ReadProcessMemory [UPPx64] ERROR ({lastWin32Error})");
                Environment.Exit(lastWin32Error);
            }
            upp = ByteArrayToStructure<RTL_USER_PROCESS_PARAMETERS_x64_TRUNCATED>(uppBuffer);
            byte[] environmentBuffer = new byte[upp.EnvironmentSize];
            if (!ReadProcessMemory(hProcess, upp.Environment, environmentBuffer, (uint)environmentBuffer.Length, out _))
            {
                int lastWin32Error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"ReadProcessMemory [ENVx64] ERROR ({lastWin32Error})");
                Environment.Exit(lastWin32Error);
            }
            return environmentBuffer;
        }
        catch (Exception ex)
        {
            MiniExceptionHandler(ex);
            Environment.Exit(-8);
            return null;
        }
    }

    // Converts a valid byte[] environment block to a string[], discarding invalid data, otherwise returns null.
    static string[] EnvironmentBlockToArray(byte[] environmentBlock)
    {
        try
        {
            string fullBlock = Encoding.Unicode.GetString(environmentBlock);
            if (string.IsNullOrEmpty(fullBlock))
            {
                return null;
            }
            List<string> list = new List<string>();
            foreach (string variable in fullBlock.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] split = variable.Split('=');
                if (split.Length == 2)
                {
                    if (!string.IsNullOrEmpty(split[0].Trim()) && !string.IsNullOrEmpty(split[1].Trim()))
                    {
                        list.Add(variable);
                    }
                }
            }
            if (list.Count > 0)
            {
                return list.ToArray();
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Converts a valid string[] environment block to a Dictionary<string, string>, discarding invalid data, otherwise returns null.
    static Dictionary<string, string> EnvironmentArrayToDict(string[] environmentArray)
    {
        if (environmentArray.Length == 0)
        {

            // This will never happen with my unmodified code, but it's here for you.

            Console.Error.WriteLine("EMPTY ENVIRONMENT ARRAY!");
            return null;
        }
        Dictionary<string, string> dict = new Dictionary<string, string>();
        foreach (string variable in environmentArray)
        {
            string[] split = variable.Split('=');

            // Always returns true with my unmodified code, but it's here for you.
            if (split.Length == 2)
            {

                // Always returns !false && !false with my unmodified code, but it's here for you.
                if (!string.IsNullOrEmpty(split[0].Trim()) && !string.IsNullOrEmpty(split[1].Trim()))
                {
                    dict[split[0]] = split[1];
                }
            }
        }
        if (dict.Count > 0)
        {
            return dict;
        }

        // This will never happen with my unmodified code, but it's here for you.

        Console.Error.WriteLine("INVALID ENVIRONMENT ARRAY!");
        return null;
    }

    // Converts a byte[] from the ReadProcessMemory Windows function to a properly formatted struct (if the struct is correctly defined)
    // With my unmodified code, the structs are truncated rather than properly formatted, however, preventing errors and saving memory.
    static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
    {
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
        }
        finally
        {
            handle.Free();
        }
    }

    // My own personal simple exception "handler", to prevent the same repetitive code over and over again.
    static void MiniExceptionHandler(Exception ex)
    {
        if (string.IsNullOrEmpty(ex.Message))
        {
            Console.Error.WriteLine(ex.GetType().ToString());
        }
        else
        {
            Console.Error.WriteLine(ex.Message);
        }
    }

}

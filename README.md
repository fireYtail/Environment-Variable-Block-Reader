# Environment-Variable-Block-Reader

This software allows you to read any currently running Windows process environment variable block by specifying its process ID, this data can be saved as file(s) or processed for your specific use case.

The compiled version requires .NET Framework Runtime v4.6 or above. However, you can take the source code to remove this requirement, build it with a different language, or change the software, partially or completely, to meet your specific needs.

It is an "any CPU" build, so you can run this on 32 bit Windows. It defaults to 64 bits mode, since a 32 bit proccess can't read the environment block of a 64 bit process, but in 64 bits mode it can read both target types without an issue.

Although not strictly speaking a requirement, running this software with elevation and with SeDebugPrivilege will be the way to go. It is a console application, so take a look at the syntax on the console, or go all in and read the source code (with some comments inside, to help you figure things out easier)

Hope you can give it some good use, although it's not meant to be a "final app", but rather, a good, useful, and very importantly ACTUALLY WORKING starting point. Microsoft doesn't provide any means of reading a different process' variables, and documentation on this "procedure" is scarce and not easy to come accross or not useful.

MIT license, proper credit is appreciated.

## Exit codes

As with any console application, EVBR will always exit with a numeric code:

  * 0, on success. Also returned if no arguments were given (shows the usage help)
  * -1, if the first argument syntax is invalid.
  * -2, if the second argument syntax is invalid.
  * -3, if more than two arguments were given. Make sure to use quotes properly.
  * -4, if the required argument pid=[PROCESS] was not given.
  * -5, if the first and the second arguments are the same type of argument.
  * -6, if the specified process ID doesn't match a currently running process.
  * -7, if getting the pointer to the memory address of the UPP struct failed (This error may return a positive code instead, see below)
  * -8, if getting the environment block failed (This error may return a positive code instead, see below)
  * -9, if EVBR is running as a 32 bit process but the target is a 64 bit process.
  * -10, if exporting the environment block to a UTF-8 file failed.
  * -11, if exporting the environment block to a UTF-16 file failed.
  * -12, if there was at least one error while exporting the environment block as multiple files.
  * -13, if your partially modified source code didn't exit by the end of Main.
  * Positive exit codes for Win32Exceptions when calling NtQueryInformationProcess() or ReadProcessMemory(), see below for more info.

Besides exiting with the mentioned codes, EVBR will show an error message on the console / standard error. Errors that are part of EVBR are in English. Exceptions should show their localized strings from Windows, or if none is available, the exception type. Note that exits with a positive code will only show the error number and not a localized string.

## Error messages with codes

If you get one of the following error messages, where ### is a number...

  * NtQueryInformationProcess ERROR (###)

  * ReadProcessMemory [PBI] ERROR (###)

  * ReadProcessMemory [UPPx86] ERROR (###)

  * ReadProcessMemory [ENVx86] ERROR (###)

  * ReadProcessMemory [UPPx64] ERROR (###)

  * ReadProcessMemory [ENVx64] ERROR (###)

...then you can look up what exactly went wrong on the links below:

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--500-999-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1000-1299-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1300-1699-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1700-3999-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--4000-5999-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--6000-8199-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--8200-8999-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--9000-11999-

https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--12000-15999-

# Environment-Variable-Block-Reader
This software allows you to read any currently running Windows process environment variable block by specifying its process ID, this data can be saved as file(s) or processed for your specific use case.

The compiled version requires .NET Framework Runtime v4.6 or above. However, you can take the source code to remove this requirement, build it with a different language, or change the software, partially or completely, to meet your specific needs.

It is an "any CPU" build, so you can run this on 32 bit Windows. It defaults to 64 bits mode, since a 32 bit proccess can't read the environment block of a 64 bit process, but in 64 bits mode it can read both target types without an issue.

Although not strictly speaking a requirement, running this software with elevation and with SeDebugPrivilege will be the way to go. It is a console application, so take a look at the syntax on the console, or go all in and read the source code (with some comments inside, to help you figure things out easier)

Hope you can give it some good use, although it's not meant to be a "final app", but rather, a good, useful, and very importantly ACTUALLY WORKING starting point. Microsoft doesn't provide any means of reading a different process' variables, and documentation on this "procedure" is scarce and not easy to come accross or not useful.

MIT license, proper credit is appreciated.

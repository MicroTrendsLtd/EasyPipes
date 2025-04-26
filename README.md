# EasyPipes
**Target Framework:** .NET Standard 2.0
A robust, flexible named-pipes library for inter-process communication on Windows.  
Uses a 4-byte header to enable transfers of up to 4 GB of data (OS-limited). Provides easy-to-use server (`Server.cs`) and client (`Client.cs`) implementations with fully customizable pipe settings. Supports both synchronous and asynchronous operations—ideal for high-performance, low-latency messaging systems.

## Background
- This was written in .net standard to allow .net 4 apps to speak to .net 6/8 and this means it has limitations noted in the code.
- This uses a 4-byte header allowing maximum message sizes, which consists of a token and the bytes sending for the message
- The client will then be able to find the start and end of the message and read in the bytes resulting in super fast binary transfer and reading!
- I didn't try 2 way with it.... as features are limited  in standard, and my requirement was one-way so far
- Robust Added some fault tolerance for connections, reconnections, and pipe disposal on a timeout

---
## Features
- **High-capacity**: 4-byte header supports up to 4 GB messages  
- **Configurable**: Pipe name, direction, security, buffer sizes, etc.  
- **Dual API**: Simple `Server` and `Client` classes  
- **Sync & Async**: Use blocking or asynchronous methods as needed  
- **Lightweight**: No external dependencies beyond .NET Standard  

---
**API Overview**

Server Class
- Constructor: Server(string pipeName)
- StartAsync: Begin listening for client connections
- StopAsync: Gracefully stop the server
- TrySendMessageAsync(string payload): Send a UTF-8 message over the pipe

Client Class
- Constructor: Client(string pipeName)
- MessageReceived: Event raised when a full message arrives (MessageEventArgs.Message.Body)

**Contributing**
- Definitely could be good to add support for other frameworks and open up more features, or port it to .net8 etc.
- also 2 way IO could be fun
1. Fork the repository
2. Create a feature branch (git checkout -b feature/YourFeature)
3. Commit your changes (git commit -m "Add YourFeature")
4. Push to your branch (git push origin feature/YourFeature)
5. Open a Pull Request
---

## Quick Start
1. Download zip and open the solution "EasyPipes.sln"
2. Compile all

**2. Pipe Server**
- Set the start up project as EasyPipeServerTest
- Run the project

**3. Client Example**
- open the solution "EasyPipes.sln" again in new instance of Visual Studio
- Set the start up project as EasyPipeServerTest
- Run the project

**Expected results**
  When the server detects the client connection it will send print to console the stats
  client will print to console the data messages received

# EasyPipes
**Target Framework:** .NET Standard 2.0
A robust, flexible named-pipes library for inter-process communication on Windows.  
Allows transfers of a message size of 4 GB of data. Provides easy-to-use server (`Server.cs`) and client (`Client.cs`) implementations with fully customizable pipe settings. It supports both synchronous and asynchronous operations and is ideal for high-performance, low-latency messaging systems.

![image](https://github.com/user-attachments/assets/d2f707f8-0628-47d1-9ddf-3a2468c14026)


## Background
- This was written in .net Standard to allow .net 4 apps to speak to .net 6/8 
- Implements 1-way server-to-client IO with very large messaging capacity via message header patterns
- Messages have a 2-byte token followed by a 4-byte length header to provide the message payload size 
- The client can find the start and end of the message bytes, resulting in super-fast binary transfer and reading!
- Robust: Fault tolerance for sending messages, reconnections, and pipe disposal on timeouts

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
- Other frameworks and open up more features, or port it to .net8 etc.
-  2 way IO could be added
1. Fork the repository
2. Create a feature branch (git checkout -b feature/YourFeature)
3. Commit your changes (git commit -m "Add YourFeature")
4. Push to your branch (git push origin feature/YourFeature)
5. Open a Pull Request
---

## Quick Start
1. Download the zip and open the solution "EasyPipes.sln"
2. Compile all

**2. Pipe Server**
- Set the start-up project as EasyPipeServerTest
- Run the project

**3. Client Example**
- Open the solution "EasyPipes.sln" again in a new instance of Visual Studio
- Set the start-up project as EasyPipeServerTest
- Run the project

**Expected results**
  - Pipe Server App detects the client connection, it will print to the console the stats.
  - Pipe Client App will print to the console the data messages received

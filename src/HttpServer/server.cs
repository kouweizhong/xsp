//
// server.cs: Http server for ASP pages.
//
// Authors:
//	Gonzalo Paniagua Javier (gonzalo@ximian.com)
//
// Licensed under the terms of the GNU GPL
//
// (C) 2002 Ximian, Inc (http://www.ximian.com)
//

namespace Mono.ASP {

using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.UI;

class MyCapabilities : HttpBrowserCapabilities
{
	private Hashtable capabilities;

	public MyCapabilities ()
	{
		capabilities = new Hashtable ();
	}
	
	public void Add (string key, string value)
	{
		capabilities.Add (key, value);
	}

	public override string this [string value]
	{
		get { return capabilities [value] as string; }
	}
}

class MyWorkerRequest
{
	private string fileName;
	private string csFileName;
	private TextReader input;
	private TextWriter output;
	private ArrayList cscOptions;
	private string className;
	private string full_request;
	private string method;
	private string query;
	private string query_options;

	private MyWorkerRequest ()
	{
	}

	public MyWorkerRequest (TextReader input, TextWriter output)
	{
		if (input == null || output == null)
			throw new ArgumentNullException ();

		this.input = input;
		this.output = output;
		cscOptions = new ArrayList ();
		cscOptions.Add ("/noconfig");
		cscOptions.Add ("/nologo");
		cscOptions.Add ("/debug+");
		cscOptions.Add ("/debug:full");
		cscOptions.Add ("/target:library");
		AddReference ("mscorlib.dll");
		AddReference ("System.dll");
		AddReference (".\\MyForm.dll");
		AddReference (Server.SystemWeb);
		AddReference (Server.SystemDrawing);
	}

	public void ProcessRequest ()
	{
		GetRequest ();
		if (!fileName.EndsWith (".aspx"))
			return;

		if (Xsp () == false){
			Console.WriteLine ("Error running xsp. Take a look at the output file.");
			return;
		}
		StreamReader st_file = new StreamReader (File.OpenRead ("output\\" + csFileName));
		StringReader file_content = new StringReader (st_file.ReadToEnd ()); //FIXME
		st_file.Close ();
		if (GetBuildOptions (file_content) == false)
			return;

		Page page = Build ();
		if (page == null){
			Console.WriteLine ("Error creating the instace of the generated class.");
			return;
		}

		SetupPage (page);
	}
	
	private void GetRequest ()
	{
		full_request = input.ReadLine ();
		string req = full_request.Trim ();
		if (0 == String.Compare ("GET ", req.Substring (0, 4), true))
			method = "GET";
		else if (0 == String.Compare ("POST ", req.Substring (0, 5), true))
			method = "POST";
		else
			throw new InvalidOperationException ("Unrecognized method in query: " + full_request);

		fileName = full_request.Substring (method.Length);
		fileName = fileName.Trim ();
		if (fileName [0] == '/')
			fileName = fileName.Substring (1);

		int end = fileName.IndexOf (' ');
		if (end != -1)
			fileName = fileName.Substring (0, end);
		Console.WriteLine ("File name: {0}", fileName);
		csFileName = fileName.Replace (".aspx", ".cs");
	}
	
	private bool RunProcess (string exe, string arguments, string output_file, string bat_file)
	{
		Console.WriteLine ("{0} {1}", exe, arguments);
		Console.WriteLine ("Output goes to {0}", output_file);
		Console.WriteLine ("Bat file is {0}", bat_file);

		Process proc = new Process ();
		proc.StartInfo.FileName = exe;
		proc.StartInfo.Arguments = arguments;
		proc.StartInfo.UseShellExecute = false;
		proc.StartInfo.RedirectStandardOutput = true;
		proc.Start ();
		string poutput = proc.StandardOutput.ReadToEnd();
		proc.WaitForExit ();
		int result = proc.ExitCode;
		proc.Close ();

		StreamWriter cmd_output = new StreamWriter (File.Create (output_file));
		cmd_output.Write (poutput);
		cmd_output.Close ();
		StreamWriter bat_output = new StreamWriter (File.Create (bat_file));
		bat_output.Write (exe + " " + arguments);
		bat_output.Close ();

		return (result == 0);
	}

	private bool Xsp ()
	{
		return RunProcess ("xsp.exe", 
				   fileName, 
				   "output\\" + csFileName, 
				   "output\\xsp_" + fileName + ".bat");
	}

	private HttpBrowserCapabilities GetCapabilities ()
	{
		MyCapabilities capab = new MyCapabilities ();
		capab.Add ("Accept", "*/*");

		capab.Add ("Referer", "http://127.0.0.1/");
		capab.Add ("Accept-Language", "es");
		capab.Add ("Accept-Encoding", "gzip, deflate");
		capab.Add ("User-Agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.0; .NET CLR" +
			   " 1.0.3705)");
		capab.Add ("Host", "127.0.0.1");
		return capab;
	}

	private void SetupPage (Page page)
	{
		HttpRequest request = new HttpRequest (fileName, "http://127.0.0.1/" + fileName, "");
		request.Browser = GetCapabilities ();
		HttpResponse response = new HttpResponse (output);
		page.ProcessRequest (new HttpContext (request, response));
	}

	private void AddReference (string reference)
	{
		cscOptions.Add ("/r:" + reference);
	}
	
	private string GetAttributeValue (string line, string att)
	{
		string att_start = att + "=\"";
		int begin = line.IndexOf (att_start);
		int end = line.Substring (begin + att_start.Length).IndexOf ('"');
		if (begin == -1 || end == -1)
			throw new ApplicationException ("Error in reference option:\n" + line);

		return line.Substring (begin + att_start.Length, end);
	}
	
	private bool GetBuildOptions (StringReader genCode)
	{
		string line;
		string dll;

		while ((line = genCode.ReadLine ()) != String.Empty){
			if (line.StartsWith ("//<class ")){
				className = GetAttributeValue (line, "name");
			}
			else if (line.StartsWith ("//<compileandreference ")){
				string src = GetAttributeValue (line, "src");
				dll = src.Replace (".cs", ".dll"); //FIXME
				//File.Delete (dll);
				if (Compile (src, dll) == false){
					Console.WriteLine ("Error compiling {0}. See the output file.", src);
					return false;
				}
				AddReference (dll.Replace (".dll", ""));
			}
			else if (line.StartsWith ("//<reference ")){
				dll = GetAttributeValue (line, "dll");
				AddReference (dll);
			}
			else {
				Console.WriteLine ("This is the build option line i get:\n" + line);
				return false;
			}
		}

		return true;
	}

	private Page Build ()
	{
		string dll = "output\\" + fileName.Replace (".aspx", ".dll");
		//File.Delete (dll);
		Compile (fileName.Replace (".aspx", ".cs"), dll); // May be this fails cause we are locking 
								  // the dll

		Assembly page_dll = Assembly.LoadFrom (dll);
		if (page_dll == null){
			Console.WriteLine ("Error loading generated dll: " + dll);
			return null;
		}

		return (Page) page_dll.CreateInstance ("ASP." + className);
	}

	private bool Compile (string csName, string dllName)
	{
		string [] args = new string [cscOptions.Count + 2];
		int i = 0;
		foreach (string option in cscOptions)
			args [i++] = option;

		args [i++] = "/out:" + dllName;
		args [i] = "output\\" + csName;

		string cmdline = "";
		foreach (string arg in args)
			cmdline += " " + arg;

		string noext = csName.Replace (".cs", "");
		string output_file = "output\\output_from_compilation_" + noext + ".txt";
		string bat_file = "output\\last_compilation_" + noext + ".bat";
		return RunProcess ("csc.exe", cmdline, output_file, bat_file);
	}
}

class Worker
{
	private TcpClient socket;
	
	public Worker (TcpClient socket)
	{
		this.socket = socket;
	}

	public void Run ()
	{
		Console.WriteLine ("Started processing...");
		HtmlTextWriter output = new HtmlTextWriter (new StreamWriter (socket.GetStream ()));
		StreamReader input = new StreamReader (socket.GetStream ());
		try {
			MyWorkerRequest proc = new MyWorkerRequest (input, output);
			proc.ProcessRequest ();
		} catch (Exception e) {
			Console.WriteLine ("Caught exception in Worker.Run");
			Console.WriteLine (e.ToString ());
			output.WriteLine ("<html>\n<title>Error</title>\n<body>\n<pre>\n" + e.ToString () +
					  "\n</pre>\n</body>\n</html>\n");
			output.Close ();
		}

		// output is closed in Page.ProcessRequest
		input.Close ();
		socket.Close ();
		Console.WriteLine ("Finished processing...");
	}
}

public class Server
{
	private TcpListener listen_socket;
	private bool started;
	private bool stop;
	private Thread runner;
	private IPEndPoint bind_address;
	private ArrayList workers;

	public Server ()
		: this (IPAddress.Any, 80)
	{
	}

	public Server (int port)
		: this (IPAddress.Any, port)
	{
	}

	public Server (IPAddress address, int port) 
		: this (new IPEndPoint (address, port))
	{
	}
	
	public Server (IPEndPoint bindAddress)
	{
		if (bindAddress == null)
			throw new ArgumentNullException ("bindAddress");

		bind_address = bindAddress;
	}

	public void Start ()
	{
		if (started)
			throw new InvalidOperationException ("The server is already started.");

		workers = new ArrayList ();
		listen_socket = new TcpListener (bind_address);
		listen_socket.Start ();
		runner = new Thread (new ThreadStart (RunServer));
		runner.Start ();
		stop = false;
		Console.WriteLine ("Server started.");
	}

	public void Stop ()
	{
		if (!started)
			throw new InvalidOperationException ("The server is not started.");

		stop = true;	
		listen_socket.Stop ();
		foreach (Thread th in workers)
			if (th.ThreadState != System.Threading.ThreadState.Stopped)
				th.Abort ();
		workers = null;
		Console.WriteLine ("Server stopped.");
	}

	private void RunServer ()
	{
		started = true;
		try {
			TcpClient client;
			int nrequest = 0;
			while (!stop){
				client = listen_socket.AcceptTcpClient ();
				nrequest++;
				if (nrequest % 1000 == 0)
					CleanupWorkers ();

				Console.WriteLine ("Accepted connection.");
				Worker one_shot = new Worker (client);
				Thread worker = new Thread (new ThreadStart (one_shot.Run));
				workers.Add (worker);
				worker.Start ();
			}
		} catch (ThreadAbortException){
		}

		started = false;
	}
	
	private void CleanupWorkers ()
	{
		ArrayList new_workers = new ArrayList ();

		foreach (Thread th in workers)
			if (th.ThreadState != System.Threading.ThreadState.Stopped)
				new_workers.Add (th);

		workers = new_workers;
	}
	
	private static bool useMonoClasses;

	public static bool UseMonoClasses
	{
		get { return useMonoClasses; }
	}

	public static string SystemWeb
	{
		get { return (!useMonoClasses ? "System.Web.dll" : ".\\lib\\System.Web.dll"); }
	}

	public static string SystemDrawing
	{
		get { return (!useMonoClasses ? "System.Drawing.dll" : ".\\lib\\System.Drawing.dll"); }
	}

	private static void Usage ()
	{
		Console.WriteLine ("Usage: server [--usemonoclasses] port");
		Console.WriteLine ("By default, it uses csc to compile against mono " +
				   "System.Web and System.Color, which must be copied\n" +
				   "to the directory where you run the server.");
		Environment.Exit (1);
	}

	public static int Main (string [] args)
	{
		if (args.Length == 0 || args.Length > 3)
			Usage ();

		int port = 80;
		bool useMonoClasses_set = false;
		bool port_set = false;
		foreach (string arg in args){
			if (!useMonoClasses_set && 0 == String.Compare (arg, "--usemonoclasses")){
				useMonoClasses = true;
				useMonoClasses_set = true;
			}
			else if (!port_set){
				try {
					port = Int32.Parse (arg);
					port_set = true;
				} catch (Exception){
					Usage ();
				}
			}
			else
				Usage ();
		}

		if (!Directory.Exists ("output")){
			Console.WriteLine ("Creating directory 'output' where BAT files and \n" + 
					   "comand output will be stored.");
			Directory.CreateDirectory ("output");
		}

		Console.WriteLine ("Remember that you should rerun the server if you change\n" + 
				   "the aspx file!");

		Server server = new Server (port);
		server.Start ();
		return 0;
	}
}

}


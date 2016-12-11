using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Text;
using WebSocket4Net;
using SuperSocket.ClientEngine;
using System.ComponentModel;

namespace Client
{
	public static class Extensions
	{
		public static object Get(this Dictionary<string,object> obj,string key)
		{
			object tmp=null;
			obj.TryGetValue(key,out tmp);
			return tmp;
		}
	}

	public enum JoinRequestResultReason
	{
		Succeeded=0,NameAlreadyUsed=1,InvalidRequest=2,AlreadyConnected=3,UnknownError=4
	}

	public enum PowerEventKind
	{
		Shutdown=0,Reboot=1,Suspend=2,Hibernate=3
	}

	public class JoinResponseReceivedEventArgs:EventArgs
	{
		public bool Result{get;private set;}
		public JoinRequestResultReason Reason {get;private set;}

		public JoinResponseReceivedEventArgs(bool result,JoinRequestResultReason reason)
		{
			Result=result;
			Reason=reason;
		}
	}

	public class OpenCommandReceivedEventArgs:EventArgs
	{
		public string ResourceName{get;private set;}

		public OpenCommandReceivedEventArgs(string resourceName)
		{
			ResourceName=resourceName;
		}
	}

	public class ExecuteCommandReceivedEventArgs:EventArgs
	{
		public string CommandName{get;private set;}
		public string Arguments{get;private set;}

		public ExecuteCommandReceivedEventArgs(string commandName,string arguments)
		{
			CommandName=commandName;
			Arguments=arguments;
		}
	}

	public class MessageReceivedEventArgs:EventArgs
	{
		public string Name{get;private set;}
		public string Text{get;private set;}

		public MessageReceivedEventArgs(string name,string text)
		{
			Name=name;
			Text=text;
		}
	}

	public class ScreenShotRequestReceivedEventArgs:EventArgs
	{
		public Size Size{get;private set;}

		public ScreenShotRequestReceivedEventArgs(Size size)
		{
			Size=size;
		}
	}

	public class UserEventArgs:EventArgs
	{
		public string UserName{get;private set;}
		public Color Color{get;private set;}

		public UserEventArgs(string userName,Color color)
		{
			UserName=userName;
			Color=color;
		}
	}

	public class ServerInfoReceivedEventArgs
	{
		public Dictionary<string,Color> Users{get;private set;}
		
		public ServerInfoReceivedEventArgs(Dictionary<string,Color> users)
		{
			Users=users;
		}
	}

	public class PowerEventReceivedEventArgs
	{
		public PowerEventKind EventKind{get;private set;}

		public PowerEventReceivedEventArgs(PowerEventKind eventKind)
		{
			EventKind=eventKind;
		}
	}

	public enum ErrorReason
	{
		ConnectionError=0,DataSendError=1,UnknownError=2
	}

	public class ErrorEventArgs:EventArgs
	{
		public ErrorReason Reason{get;private set;}

		public ErrorEventArgs(ErrorReason reason)
		{
			Reason=reason;
		}
	}

	public enum ServerState
	{
		Left=0,Connecting=1,Connected=2,Joining=3
	}

	public class MessageServer
	{
		WebSocket socket;
		const int port=7345;
		Json.Json json;
		bool leaveRequestHasSent;
		string sentName;
		ISynchronizeInvoke syncObj;

		public event Action<JoinResponseReceivedEventArgs> JoinResponseReceived;
		public event Action<ServerInfoReceivedEventArgs> ServerInfoReceived;
		public event Action<OpenCommandReceivedEventArgs> OpenCommandReceived;
		public event Action<ExecuteCommandReceivedEventArgs> ExecuteCommandReceived;
		public event Action<MessageReceivedEventArgs> MessageReceived;
		public event Action<ScreenShotRequestReceivedEventArgs> ScreenShotRequestReceived;
		public event Action<UserEventArgs> UserAdded;
		public event Action<UserEventArgs> UserRemoved;
		public event Action<PowerEventReceivedEventArgs> PowerEventReceived;
		public event Action<EventArgs> Left;
		public event Action<ErrorEventArgs> Error;
		public ServerState State{get;private set;}
		public string Name{get;private set;}
		public string OperatorName{get;private set;}

		public MessageServer(Form owner)
		{
			json=new Json.Json();
			socket=null;
			State=ServerState.Left;
			Name=null;
			leaveRequestHasSent=false;
			syncObj=owner;
		}

		public void Join(string address,string name)
		{
			if(socket!=null&&socket.State==WebSocketState.Open) throw new InvalidOperationException();
			sentName=name;
			socket=CreateSocket(address);
			socket.Opened+=(sender,e)=>{
				State=ServerState.Connected;
				SendJoinRequest(name);
			};
			socket.Open();
			State=ServerState.Connecting;
		}

		public void Leave()
		{
			if(socket==null||socket.State==WebSocketState.Closed||socket.State==WebSocketState.None)
				throw new InvalidOperationException();
			SendLeaveRequest();
		}

		private void ProcessMessage(Dictionary<string,object> obj)
		{
			switch(obj.Get("type") as string){
			case "join":
				OnJoinResponseReceived(obj);
				break;
			case "serverinfo":
				OnServerInfoReceived(obj);
				break;
			case "open":
				OnOpenCommandReceived(obj);
				break;
			case "execute":
				OnExecuteCommandReceived(obj);
				break;
			case "message":
				OnMessageReceived(obj);
				break;
			case "screenshot":
				OnScreenShotRequestReceived(obj);
				break;
			case "adduser":
				OnUserAdded(obj);
				break;
			case "deluser":
				OnUserRemoved(obj);
				break;
			case "power":
				OnPowerEventReceived(obj);
				break;
			default:
				break;
			}
		}

		private void OnJoinResponseReceived(Dictionary<string,object> obj)
		{
			if(obj.Get("result") is bool&&obj.Get("reason") is long){
				var result=(bool)obj["result"];
				var reason=(JoinRequestResultReason)(long)obj["reason"];
				if(result){
					State=ServerState.Joining;
					Name=sentName;
				}
				if(JoinResponseReceived!=null)
					syncObj.BeginInvoke((Action)(()=>JoinResponseReceived(new JoinResponseReceivedEventArgs(result,reason))),null);
			}
		}

		private void OnServerInfoReceived(Dictionary<string,object> obj)
		{
			if(obj.Get("operatorname") is string&&obj.Get("users") is List<object>&&ServerInfoReceived!=null){
				OperatorName=(string)obj["operatorname"];
				var users=new Dictionary<string,Color>();
				((List<object>)obj["users"]).Cast<Dictionary<string,object>>().ToList().ForEach(
					user=>{
						if(user.Get("name") is string&&user.Get("color") is long){
							users.Add((string)user["name"],Color.FromArgb((int)(long)user["color"]));
						}else throw new InvalidOperationException();
					});
				syncObj.BeginInvoke((Action)(()=>ServerInfoReceived(new ServerInfoReceivedEventArgs(users))),null);
			}
		}

		private void OnOpenCommandReceived(Dictionary<string,object> obj)
		{
			if(obj.Get("resourcename") is string&&OpenCommandReceived!=null){
				var resourceName=(string)obj["resourcename"];
				syncObj.BeginInvoke((Action)(()=>OpenCommandReceived(new OpenCommandReceivedEventArgs(resourceName))),null);
			}
		}

		private void OnExecuteCommandReceived(Dictionary<string,object> obj)
		{
			if(obj.Get("command") is string&&obj.Get("arguments") is string&&ExecuteCommandReceived!=null){
				var commandName=(string)obj["command"];
				var arguments=(string)obj["arguments"];
				syncObj.BeginInvoke((Action)(()=>ExecuteCommandReceived(new ExecuteCommandReceivedEventArgs(commandName,arguments))),null);
			}
		}

		private void OnMessageReceived(Dictionary<string,object> obj)
		{
			if(obj.Get("name") is string&&obj.Get("text") is string&&MessageReceived!=null){
				var name=(string)obj["name"];
				var text=(string)obj["text"];
				syncObj.BeginInvoke((Action)(()=>MessageReceived(new MessageReceivedEventArgs(name,text))),null);
			}
		}

		private void OnScreenShotRequestReceived(Dictionary<string,object> obj)
		{
			if(obj.Get("width") is long&&obj.Get("height") is long&&ScreenShotRequestReceived!=null){
				var size=new Size((int)(long)obj["width"],(int)(long)obj["height"]);
				syncObj.BeginInvoke((Action)(()=>ScreenShotRequestReceived(new ScreenShotRequestReceivedEventArgs(size))),null);
			}
		}

		private void OnUserAdded(Dictionary<string,object> obj)
		{
			if(obj.Get("name") is string&&obj.Get("color") is long&&UserAdded!=null){
				var name=(string)obj["name"];
				var color=Color.FromArgb((int)(long)obj["color"]);
				syncObj.BeginInvoke((Action)(()=>UserAdded(new UserEventArgs(name,color))),null);
			}
		}

		private void OnUserRemoved(Dictionary<string,object> obj)
		{
			if(obj.Get("name") is string&&UserRemoved!=null){
				var name=(string)obj["name"];
				syncObj.BeginInvoke((Action)(()=>UserRemoved(new UserEventArgs(name,Color.Empty))),null);
				if(name==Name&&leaveRequestHasSent){
					leaveRequestHasSent=false;
					socket.Close();
				}
			}
		}

		private void OnPowerEventReceived(Dictionary<string,object> obj)
		{
			if(obj.Get("kind") is long&&PowerEventReceived!=null){
				var kind=(PowerEventKind)(long)obj.Get("kind");
				PowerEventReceived(new PowerEventReceivedEventArgs(kind));
			}
		}

		private void OnLeft()
		{
			if(Left!=null) syncObj.BeginInvoke((Action)(()=>Left(new EventArgs())),null);
		}

		private void OnError(ErrorEventArgs args)
		{
			if(Error!=null) syncObj.BeginInvoke((Action)(()=>Error(args)),null);
		}

		private void OnMessageReceived(object sender,WebSocket4Net.MessageReceivedEventArgs e)
		{
			var obj=json.Parse(e.Message);
			if(obj is Dictionary<string,object>) ProcessMessage((Dictionary<string,object>)obj);
		}

		private void OnDataReceived(object sender,DataReceivedEventArgs e)
		{
		}

		private void OnClosed(object sender,EventArgs e)
		{
			Name=null;
			State=ServerState.Left;
			socket=null;
			OnLeft();
		}

		private void OnError(object sender,SuperSocket.ClientEngine.ErrorEventArgs e)
		{
			var reason=ErrorReason.UnknownError;
			if(socket!=null&&socket.State==WebSocketState.Connecting) reason=ErrorReason.ConnectionError;
			else if(socket!=null&&socket.State==WebSocketState.Open) reason=ErrorReason.DataSendError;
			State=ServerState.Left;
			OnError(new ErrorEventArgs(reason));
		}

		private void Send(string message)
		{
			if(socket==null) throw new InvalidOperationException();
			socket.Send(message);
		}

		private void Send(byte[] data)
		{
			if(socket==null) throw new InvalidOperationException();
			socket.Send(data,0,data.Length);
		}

		private void SendBinaryData(string type,byte[] content)
		{
			var data=Encoding.UTF8.GetBytes(type).Concat(new byte[]{0}).Concat(content).ToArray();
			Send(data);
		}

		private void SendJoinRequest(string name)
		{
			var obj=new Dictionary<string,object>(){
				{"type","join"},{"name",name},{"machinename",Environment.MachineName}
			};
			Send(json.ToString(obj));
		}

		private void SendLeaveRequest()
		{
			var obj=new Dictionary<string,object>(){
				{"type","leave"}
			};
			Send(json.ToString(obj));
			leaveRequestHasSent=true;
		}

		public void SendMessage(string text)
		{
			var obj=new Dictionary<string,object>(){
				{"type","message"},{"text",text}
			};
			Send(json.ToString(obj));
		}

		public void SendScreenShotData(byte[] data)
		{
			SendBinaryData("screenshot",data);
		}

		private WebSocket CreateSocket(string address)
		{
			var socket=new WebSocket("ws://"+address+":"+port.ToString(),null,WebSocketVersion.Rfc6455);
			socket.MessageReceived+=OnMessageReceived;
			socket.DataReceived+=OnDataReceived;
			socket.Closed+=OnClosed;
			socket.Error+=OnError;
			return socket;
		}
	}

	static class Program
	{
		/// <summary>
		/// アプリケーションのメイン エントリ ポイントです。
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new Form1());
		}
	}
}

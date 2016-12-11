using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading.Tasks;

namespace Client
{
	//切断時にリストの整合性が取れていない
	//bindingsourceが原因っぽい？
	//dataviewを使う？
	public partial class Form1 : Form
	{
		MessageServer server;
		EncoderParameters encoderParams;
		ImageCodecInfo imageCodec;
		bool connected;
		UdpClient socket;
		IPAddress classABroadcastAddress=IPAddress.Parse("10.255.255.255"),localAddress,broadcastAddress;
		CancellationTokenSource cancelTokenSource;
		BindingSource userList=new BindingSource();
		Form2 fileTransferWindow;

		public Form1()
		{
			server=new MessageServer(this);
			server.JoinResponseReceived+=OnJoinResponseReceived;
			server.ServerInfoReceived+=OnServerInfoReceived;
			server.OpenCommandReceived+=OnOpenCommandReceived;
			server.ExecuteCommandReceived+=OnExecuteCommandReceived;
			server.MessageReceived+=OnMessageReceived;
			server.ScreenShotRequestReceived+=OnScreenShotRequestReceived;
			server.UserAdded+=OnUserAdded;
			server.UserRemoved+=OnUserRemoved;
			server.Left+=OnLeft;
			server.Error+=OnError;
			InitializeComponent();
			connected=false;
			encoderParams=new EncoderParameters();
			encoderParams.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,95L);
			imageCodec=ImageCodecInfo.GetImageEncoders().First(codec=>codec.MimeType=="image/png");
			localAddress=GetLocalIPAddress();
			broadcastAddress=GetBroadcastAddress(localAddress);
			socket=new UdpClient();
			socket.ExclusiveAddressUse=false;
			socket.EnableBroadcast=true;
			socket.Client.Bind(new IPEndPoint(IPAddress.Any,0));
			userList.DataSource=new List<User>();
			userList.Sort="Name";
			//DataView view=new DataView();
			listBox1.DataSource=userList;
			fileTransferWindow=new Form2();
		}

		private void OnServerInfoReceived(ServerInfoReceivedEventArgs e)
		{
			//userList.SuspendBinding();
			foreach(var user in e.Users.Select(user=>new User(user.Key,user.Value)).ToArray()) userList.Add(user);
			//userList.ResumeBinding();
		}

		private void OnJoinResponseReceived(JoinResponseReceivedEventArgs e)
		{
			var reasonText=e.Reason==JoinRequestResultReason.Succeeded?"接続に成功しました。":
				e.Reason==JoinRequestResultReason.NameAlreadyUsed?"その名前は既に使われています。":
				e.Reason==JoinRequestResultReason.InvalidRequest?"不正なリクエストです。":
				e.Reason==JoinRequestResultReason.AlreadyConnected?"あなたは既に接続しています。":"";
			ShowBalloon(reasonText,e.Reason==JoinRequestResultReason.Succeeded?ToolTipIcon.Info:ToolTipIcon.Error);
			if(e.Result){
				Text="RemoteController(Client)@"+server.Name;
				button1.Enabled=true;
				button1.Text="切断";
				textBox2.Enabled=true;
				button2.Enabled=true;
				AcceptButton=button2;
				connected=true;
				splitContainer1.Panel2Collapsed=true;
				textBox2.Focus();
			}else Leave();
		}

		private void OnOpenCommandReceived(OpenCommandReceivedEventArgs e)
		{
			try{
				Process.Start(e.ResourceName);
			}catch(Exception){}
		}

		private void OnExecuteCommandReceived(ExecuteCommandReceivedEventArgs e)
		{
			try{
				Process.Start(e.CommandName,e.Arguments);
			}catch(Exception){}
		}

		private void OnMessageReceived(MessageReceivedEventArgs e)
		{
			if(e.Text.FirstOrDefault()=='@'){
				var name=new string(Regex.Split(e.Text,"\\s")[0].Skip(1).ToArray());
				if(name==server.Name)
					ShowBalloon((e.Name==""?server.OperatorName:e.Name)+"さんからのメッセージ\n"+e.Text,ToolTipIcon.Info);
			}
			AppendLog(e.Name,(e.Name==""?server.OperatorName:e.Name)+": "+e.Text);
		}

		private void OnScreenShotRequestReceived(ScreenShotRequestReceivedEventArgs e)
		{
			ThreadPool.QueueUserWorkItem((WaitCallback)(_=>SendScreenShot(e.Size.Width,e.Size.Height)));
		}

		private void OnUserAdded(UserEventArgs e)
		{
			var name=e.UserName;
			userList.Add(new User(name,e.Color));
			AppendLog(name,name+"さんが接続しました。");
		}

		private void OnUserRemoved(UserEventArgs e)
		{
			var name=e.UserName;
			AppendLog(name,name+"さんが切断しました。");
			userList.Remove(userList.Cast<User>().First(user=>user.Name==name));
		}

		private void OnLeft(EventArgs e)
		{
			if(connected&&!this.IsDisposed){
				ShowBalloon("接続を切断しました。",ToolTipIcon.Info);
				Leave();
			}
		}

		private void OnError(ErrorEventArgs e)
		{
			if(e.Reason==ErrorReason.ConnectionError) ShowBalloon("接続に失敗しました。",ToolTipIcon.Error);
			else if(e.Reason==ErrorReason.DataSendError) ShowBalloon("データの送信に失敗しました。",ToolTipIcon.Error);
			else ShowBalloon("原因不明のエラーが発生しました。",ToolTipIcon.Error);
			Leave();
		}

		private void Form1_FormClosing(object sender,FormClosingEventArgs e)
		{
			if(server.State==ServerState.Joining){
				server.UserRemoved-=OnUserRemoved;
				server.Leave();
			}
			if(cancelTokenSource!=null&&!cancelTokenSource.IsCancellationRequested) cancelTokenSource.Cancel();
		}

		private Bitmap CreateThumbnail(Bitmap original,int width,int height)
		{
			var bmp=new Bitmap(width,height);
			var graphics=Graphics.FromImage(bmp);
			graphics.Clear(Color.White);
			float drawWidth=original.Width,drawHeight=original.Height;
			if(original.Width>width||original.Height>height){
				var scale=Math.Min((float)width/original.Width,(float)height/original.Height);
				drawWidth*=scale;
				drawHeight*=scale;
			}
			graphics.DrawImage(original,(width-drawWidth)/2,(height-drawHeight)/2,drawWidth,drawHeight);
			return bmp;
		}

		private void SendScreenShot(int width,int height)
		{
			var size=SystemInformation.VirtualScreen.Size;
			byte[] data=null;
			using(var bitmap=new Bitmap(size.Width,size.Height,PixelFormat.Format24bppRgb))
			using(var graphics=Graphics.FromImage(bitmap))
			using(var stream=new MemoryStream(1048576)){
				graphics.CopyFromScreen(Point.Empty,Point.Empty,size);
				if(width!=0&&height!=0) 
					using(var smallBitmap=CreateThumbnail(bitmap,width,height))
						smallBitmap.Save(stream,imageCodec,encoderParams);
				else bitmap.Save(stream,imageCodec,encoderParams);
				data=stream.ToArray();
			}
			try{
				server.SendScreenShotData(data);
			}catch(Exception){}
		}

		private void button1_Click(object sender,EventArgs e)
		{
			if(textBox1.Text!=""&&!Regex.IsMatch(textBox1.Text,".*\\s.*")&&maskedTextBox1.MaskCompleted){
				if(server.State==ServerState.Left) Join();
				else Leave();
			}
		}

		private void AppendLog(string name,string text)
		{
			var color=name==""?Color.Black:userList.Cast<User>().First(user=>user.Name==name).Color;
			listBox2.Items.Insert(0,new Message(name,text,color));
		}

		private void Join()
		{
			textBox1.Enabled=false;
			maskedTextBox1.Enabled=false;
			button1.Enabled=false;
			listBox1.SelectedIndex=-1;
			userList.Clear();
			server.Join(maskedTextBox1.Text.Replace(" ",""),textBox1.Text);
		}

		private new void Leave()
		{
			Text="RemoteController(Client)";
			textBox1.Enabled=true;
			maskedTextBox1.Enabled=true;
			button1.Enabled=true;
			textBox2.Enabled=false;
			button2.Enabled=false;
			button1.Text="接続";
			AcceptButton=button1;
			if(server.State==ServerState.Joining) server.Leave();
			connected=false;
			splitContainer1.Panel2Collapsed=false;
			button3_Click(null,null);
		}

		private void button2_Click(object sender,EventArgs e)
		{
			if(textBox2.Text!=""){
				server.SendMessage(textBox2.Text);
				textBox2.Text="";
			}
			textBox2.Focus();
		}

		private void Form1_Load(object sender,EventArgs e)
		{
			var args=Environment.GetCommandLineArgs();
			if(args.Length>1){
				maskedTextBox1.Text=string.Format("{0,3}.{1,3}.{2,3}.{3,3}",args[1].Split(new[]{'.'}));
				if(args.Length>2){
					textBox1.Text=args[2];
					button1_Click(null,null);
				}
			}else button3_Click(null,null);
		}

		private void listBox1_DrawItem(object sender,DrawItemEventArgs e)
		{
			if(e.Index!=-1){
				var user=(User)userList[e.Index];
				e.DrawBackground();
				e.Graphics.DrawString(user.Name,e.Font,new SolidBrush(user.Color),e.Bounds);
				e.DrawFocusRectangle();
			}
		}

		private void listBox2_DrawItem(object sender,DrawItemEventArgs e)
		{
			if(e.Index!=-1){
				var message=(Message)listBox2.Items[e.Index];
				e.DrawBackground();
				e.Graphics.DrawString(message.Text,e.Font,new SolidBrush(message.Color),e.Bounds);
				e.DrawFocusRectangle();
			}
		}

		private void ShowBalloon(string text,ToolTipIcon icon)
		{
			notifyIcon1.ShowBalloonTip(1000,"RemoteController(Client)",text,icon);
		}

		private void listBox1_DoubleClick(object sender,EventArgs e)
		{
			if(listBox1.SelectedIndex!=-1){
				var user=listBox1.SelectedItem as User;
				if(user==null) return;
				textBox2.Text="@"+user.Name+" "+textBox2.Text;
			}
		}

		private void listBox2_DoubleClick(object sender,EventArgs e)
		{
			if(listBox2.SelectedIndex!=-1){
				var message=listBox2.SelectedItem as Message;
				if(message==null) return;
				textBox2.Text="@"+message.Name+" "+textBox2.Text;
			}
		}

		private void SearchServer()
		{
			var message=Encoding.UTF8.GetBytes("Hello? Can anyone hear me?");
			socket.Send(message,message.Length,new IPEndPoint(broadcastAddress,54563));
			for(;;){
				var endPoint=new IPEndPoint(IPAddress.Any,0);
				var response=socket.Receive(ref endPoint);
				if(endPoint.Port==54563){
					var responseText=Encoding.UTF8.GetString(response);
					if(Regex.IsMatch(responseText,"Yes, I'm hearing your voice\\. My name is .+\\.")){
						var serverName=responseText.Substring(40,responseText.Length-41);
						Invoke((Action<IPAddress,string>)AddServerList,endPoint.Address,serverName);
					}
				}
			}
		}

		private void AddServerList(IPAddress serverAddress,string serverName)
		{
			listBox3.Items.Add(new{Address=serverAddress,Name=serverName});
		}

		private void button3_Click(object sender,EventArgs e)
		{
			listBox3.Items.Clear();
			button3.Enabled=false;
			timer1.Enabled=false;
			timer1.Enabled=true;
			if(cancelTokenSource!=null&&!cancelTokenSource.IsCancellationRequested) cancelTokenSource.Cancel();
			cancelTokenSource=new CancellationTokenSource();
			Task.Factory.StartNew(SearchServer,cancelTokenSource.Token);
		}

		private void listBox3_DoubleClick(object sender,EventArgs e)
		{
			if(!timer1.Enabled&&listBox3.SelectedIndex!=-1){
				var address=(IPAddress)((dynamic)listBox3.SelectedItem).Address;
				maskedTextBox1.Text=string.Format("{0,3}.{1,3}.{2,3}.{3,3}",address.GetAddressBytes().Cast<object>().ToArray());
				button1_Click(null,null);
			}
		}

		private IPAddress GetLocalIPAddress()
		{
			var unicastAddresses=from i in NetworkInterface.GetAllNetworkInterfaces()
								 where i.GetIPProperties().GatewayAddresses.Count>0
								 select i.GetIPProperties().UnicastAddresses;
			var localAddresses=new List<byte[]>();
			foreach(var ipList in unicastAddresses)
				foreach(var info in ipList)
					localAddresses.Add(info.Address.GetAddressBytes());
			var localAddressBytes=from bytes in localAddresses
								  where
								  bytes[0]==10||
								  (bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31)||
								  (bytes[0]==192&&bytes[1]==168)
								  select bytes;
			return localAddressBytes.Count()!=0?new IPAddress(localAddressBytes.ToList()[0]):null;
		}

		private IPAddress GetBroadcastAddress(IPAddress localAddress)
		{
			var addressBytes=localAddress.GetAddressBytes();
			if(addressBytes[0]==10) return classABroadcastAddress;
			else if(addressBytes[0]==172&&addressBytes[1]>=16&&addressBytes[1]<=31){
				addressBytes[2]=255;
				addressBytes[3]=255;
				return new IPAddress(addressBytes);
			}else if(addressBytes[0]==192&&addressBytes[1]==168){
				addressBytes[3]=255;
				return new IPAddress(addressBytes);
			}else return IPAddress.Broadcast;
		}

		private void timer1_Tick(object sender,EventArgs e)
		{
			cancelTokenSource.Cancel();
			timer1.Enabled=false;
			button3.Enabled=true;
		}

		private void button4_Click(object sender,EventArgs e)
		{
			fileTransferWindow.Show();
		}
	}

	public class Message
	{
		public string Name{get;protected set;}
		public string Text{get;protected set;}
		public Color Color{get;protected set;}

		public Message()
		{
			Name="";
			Text="";
			Color=Color.Empty;
		}

		public Message(string name,string text,Color color)
		{
			Name=name;
			Text=text;
			Color=color;
		}
	}

	public class User
	{
		public string Name{get;protected set;}
		public Color Color{get;protected set;}

		public User()
		{
			Name="";
			Color=Color.Empty;
		}

		public User(string name,Color color)
		{
			Name=name;
			Color=color;
		}
	}
}

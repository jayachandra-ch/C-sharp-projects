using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;
using TouchlessLib;
using MSR.LST;
using MSR.LST.Net.Rtp;
using SharpFFmpeg;
using Microsoft.DirectX.DirectSound;
using g711audio;


namespace WindowsFormsApplication2
{
    public partial class Form1 : Form
    {
       
        public static TcpClient tc;
        public static String mess = "\0 ", line = " \0";
        public static NetworkStream ns;
        public static StreamReader sr;
        public static StreamWriter sw;        
        Thread Read;
        Thread TextChat;
        // video variables
        public Thread VideoChat;
        public Thread MyVideo;
        TouchlessMgr availableCamera = null;
        TouchlessMgr myCamera = null;
        // video chat variables
        public static IPEndPoint ep;
        public RtpSession rtpSession;
        public RtpSender rtpSender;
        public MemoryStream ms;
        //voice  datamembers start
        private CaptureBufferDescription captureBufferDescription;
        private AutoResetEvent autoResetEvent;
        private Notify notify;
        private WaveFormat waveFormat;
        private Capture capture;
        private int bufferSize;
        private CaptureBuffer captureBuffer;
        private UdpClient udpClient;                //Listens and sends data on port 1550, used in synchronous mode.
        private Device device;
        private SecondaryBuffer playbackBuffer;
        private BufferDescription playbackBufferDescription;
        private Socket clientSocket;
        private bool bStop;                         //Flag to end the Start and Receive threads.
        private IPEndPoint otherPartyIP;            //IP of party we want to make a call.
        private EndPoint otherPartyEP;
        private volatile bool bIsCallActive;                 //Tells whether we have an active call.
        private Vocoder vocoder;
        private byte[] byteData = new byte[1024];   //Buffer to store the data received.
        private volatile int nUdpClientFlag;   //Flag used to close the udpClient socket.
        public Thread VoiceThread;

        // voice variables end
       
       
     
        //udp variables
        Thread ReadImg;
        UdpClient udpcli;
        IPEndPoint iep;

        public Thread ImageTopic;
        enum Command
        {
            Invite, // To initiate a call.
            Bye,    // To end a call.
            Busy,   // to indicate that the User is busy.
            OK,     // to an invite message. OK is send to indicate that call is accepted.
            Null,   //this is the case when there is No command.
        };

        //Vocoder
        enum Vocoder
        {
            ALaw,   //A-Law vocoder.
            None,   //This is the case when no Voice codec is used.
        };

        public Form1()
        {                       
            InitializeComponent();
            TextChat = new Thread(new ThreadStart(ClientConnection));
            TextChat.IsBackground = true;
            VoiceInitialization();              
        }

        public void VoiceInitialization()
        {

            try
            {
                device = new Device();
                device.SetCooperativeLevel(this, CooperativeLevel.Normal);

                CaptureDevicesCollection captureDeviceCollection = new CaptureDevicesCollection();

                DeviceInformation deviceInfo = captureDeviceCollection[0];

                capture = new Capture(deviceInfo.DriverGuid);

                short channels = 1; 
                short bitsPerSample = 16; //16Bit or we can also use alternatively  8Bits helpful in bandwidth calculation.
                int samplesPerSecond = 22050; //11KHz use 11025 , 22KHz use 22050, 44KHz use 44100 etc.

                //Now we have to Set up the wave format that is to be captured.
                waveFormat = new WaveFormat();
                waveFormat.Channels = channels;
                waveFormat.FormatTag = WaveFormatTag.Pcm;
                waveFormat.SamplesPerSecond = samplesPerSecond;
                waveFormat.BitsPerSample = bitsPerSample;
                waveFormat.BlockAlign = (short)(channels * (bitsPerSample / (short)8));
                waveFormat.AverageBytesPerSecond = waveFormat.BlockAlign * samplesPerSecond;

                captureBufferDescription = new CaptureBufferDescription();
                captureBufferDescription.BufferBytes = waveFormat.AverageBytesPerSecond / 5;
                captureBufferDescription.Format = waveFormat;

                playbackBufferDescription = new BufferDescription();
                playbackBufferDescription.BufferBytes = waveFormat.AverageBytesPerSecond / 5;
                playbackBufferDescription.Format = waveFormat;
                playbackBuffer = new SecondaryBuffer(playbackBufferDescription, device);

                bufferSize = captureBufferDescription.BufferBytes;

                bIsCallActive = false;
                nUdpClientFlag = 0;

                //Using UDP sockets
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                EndPoint ourEP = new IPEndPoint(IPAddress.Any, 1450);
                //Listen asynchronously on port 1450 for coming messages (Invite, Bye, etc).
                clientSocket.Bind(ourEP);

                //Receive data from any IP.
                EndPoint remoteEP = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));

                byteData = new byte[1024];
                //Receive data asynchornously.
                clientSocket.BeginReceiveFrom(byteData,
                                           0, byteData.Length,
                                           SocketFlags.None,
                                           ref remoteEP,
                                           new AsyncCallback(OnReceive),
                                           null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-Initialize ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text.Equals("Disconnect"))
            {
                availableCamera.Dispose();
                availableCamera = null;
                button1.Text = "Connect";
                button1.Visible = true;
                
                textBox3.Enabled = true;
                button2.Visible = true;
                button4.Visible = false;
                textBox1.AppendText("\n Client disconnected successfully\n");
                Cleanup();
            }
            if (button1.Text.Equals("Connect"))
            {
                try
                {
                    tc = new TcpClient();
                   
                    tc.Connect(textBox3.Text.Trim(), 4000);
                    textBox1.AppendText("Client is ready\n");
                    this.textBox2.KeyPress += new System.Windows.Forms.KeyPressEventHandler(CheckKeys);
                    TextChat.Start();
                   ep = new IPEndPoint(IPAddress.Parse(textBox3.Text.Trim()), 5001);

                    HookRtpEvents();
                    JoinRtpSession(Dns.GetHostName());
                    button1.Text = "Disconnect";
                    textBox3.Enabled = false;
                    button2.Visible = false;
                    button4.Visible = true;

                    udpcli = new UdpClient(6000);
                    iep = new IPEndPoint(IPAddress.Any, 0);
                   // ReadImg = new Thread(new ThreadStart(Start_Receiving_Video_Conference));
                      
                }
                catch (Exception)
                {
                }
            }
        }
        public void ClientConnection()
        {
            Invoke(new MethodInvoker(AcceptClient));
        }
        public void AcceptClient()
        {
            ns = tc.GetStream();
            sw = new StreamWriter(ns);
            sr = new StreamReader(ns);
            Read = new Thread(new ThreadStart(ReadMessage));
            Read.Start();
        }         
        private void CheckKeys(object sender, System.Windows.Forms.KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13)
            {
                
                line = textBox2.Text;
                textBox1.AppendText("Client:" + line.Trim() +"\n");
                sw.WriteLine("Client:{0}", line.Trim());
                sw.Flush();
                textBox2.Clear();
                if (line.StartsWith("bye"))
                {
                    sw.Close();
                    ns.Close();
                    tc.Close();                   
                }

            }
        }
        private void Start_Receiving_Video_Conference()
        {
            try
            {

                

                while (true)
                {

                    byte[] data = udpcli.Receive(ref iep);
                    MemoryStream ms = new MemoryStream(data);
                    pictureBox2.Image = Image.FromStream(ms);
                    ms.Flush();
                    ms.Close();
                }     



            }
            catch (Exception)
            { }

        }

        public  void ReadMessage()
        {
            try
            {
                while (true)
                {
                    mess = sr.ReadLine();
                    Invoke(new MethodInvoker(AppendLine));

                    if (mess.StartsWith("bye"))
                        break;
                }
                sr.Close();
                ns.Close();
                tc.Close();
            }
            catch
            {
            }
        }
        public void AppendLine()
        {
            textBox1.AppendText("\n"+mess.Trim()+"\n");
        }
        private void HookRtpEvents()
        {
            RtpEvents.RtpParticipantAdded += new RtpEvents.RtpParticipantAddedEventHandler(RtpParticipantAdded);
            RtpEvents.RtpParticipantRemoved += new RtpEvents.RtpParticipantRemovedEventHandler(RtpParticipantRemoved);
            RtpEvents.RtpStreamAdded += new RtpEvents.RtpStreamAddedEventHandler(RtpStreamAdded);
            RtpEvents.RtpStreamRemoved += new RtpEvents.RtpStreamRemovedEventHandler(RtpStreamRemoved);
        }
        private void JoinRtpSession(string name)
        {
            rtpSession = new RtpSession(ep, new RtpParticipant(name, name), true, true);
            rtpSender = rtpSession.CreateRtpSenderFec(name, PayloadType.Chat, null, 0, 200);
        }
        private void RtpParticipantAdded(object sender, RtpEvents.RtpParticipantEventArgs ea)
        {
            
        }

        private void RtpParticipantRemoved(object sender, RtpEvents.RtpParticipantEventArgs ea)
        {
            
        }

        private void RtpStreamAdded(object sender, RtpEvents.RtpStreamEventArgs ea)
        {
            ea.RtpStream.FrameReceived += new RtpStream.FrameReceivedEventHandler(FrameReceived);
        }

        private void RtpStreamRemoved(object sender, RtpEvents.RtpStreamEventArgs ea)
        {
            ea.RtpStream.FrameReceived -= new RtpStream.FrameReceivedEventHandler(FrameReceived);
        }

        int[] yuv;
        int[] native;
        private void FrameReceived(object sender, RtpStream.FrameReceivedEventArgs ea)
        {
           /* System.IO.MemoryStream ms = new MemoryStream(ea.Frame.Buffer);
           
            IFFmpeg.avcodec_find_decoder(IFFmpeg.CodecID.CODEC_ID_H263);
            IFFmpeg.DecodeFrame(Image.FromStream(ms), native);
            IFFmpeg.ConvertYUV2RGB(yuv, Image.FromStream(ms));
            pictureBox1.Image = Image.FromStream(ms); */
        }

        private void Cleanup()
        {
            UnhookRtpEvents();
            LeaveRtpSession();
        }
        private void UnhookRtpEvents()
        {
            RtpEvents.RtpParticipantAdded -= new RtpEvents.RtpParticipantAddedEventHandler(RtpParticipantAdded);
            RtpEvents.RtpParticipantRemoved -= new RtpEvents.RtpParticipantRemovedEventHandler(RtpParticipantRemoved);
            RtpEvents.RtpStreamAdded -= new RtpEvents.RtpStreamAddedEventHandler(RtpStreamAdded);
            RtpEvents.RtpStreamRemoved -= new RtpEvents.RtpStreamRemovedEventHandler(RtpStreamRemoved);
        }

        private void LeaveRtpSession()
        {
            if (rtpSession != null)
            {
                rtpSession.Dispose();
                rtpSession = null;
                rtpSender = null;
            }
        }


        private void Form1_Load_1(object sender, EventArgs e)
        {
            button3.Visible = false;
            button4.Visible = false;
        }
        public void Form_Closing(object sender, CancelEventArgs cArgs)
        {
            if (bIsCallActive)
            {
                UninitializeCall();
                DropCall();
                clientSocket.Close();
            }
            Application.Exit();
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {
            VoiceThread = new Thread(new ThreadStart(Call));
            VoiceThread.IsBackground = true;
            VoiceThread.Start();
                       
        }
        public void videocall()
        {
            Invoke(new MethodInvoker(crossthread3));
            
        }
        public void crossthread3()
        {
            button2.Visible = false;
            button3.Visible = false;
            VideoChat = new Thread(new ThreadStart(StartCamera));
            VideoChat.IsBackground = true;
            VideoChat.Start();
        }
        public void StartCamera()
        {
            availableCamera = new TouchlessMgr();
            availableCamera.CurrentCamera = availableCamera.Cameras[0];
            availableCamera.Cameras[0].OnImageCaptured += new EventHandler<CameraEventArgs>(ForwardCapturedVideo);
        }
        public void ForwardCapturedVideo(object sender, CameraEventArgs e)
        {
            Invoke(new MethodInvoker(delegate() { SendCapturedVideo(e); }));
        }

        int [] yuv1;
         byte [] native1;
        public void SendCapturedVideo(CameraEventArgs e)
        {
            pictureBox1.Image = e.Image;
         //   try
           // {
                ms = new MemoryStream();// Store it in Binary Array as Stream
                Image bmap;
                bmap = e.Image;
                bmap.Save(ms, ImageFormat.Jpeg);
                IFFmpeg.avcodec_find_encoder(IFFmpeg.CodecID.CODEC_ID_H263);
                IFFmpeg.ConvertRGB2YUV(e.Image, yuv);
                IFFmpeg.EncodeFrame(yuv1, native1);                             
                rtpSender.Send(ms.ToArray());
          //  }
          //  catch (Exception)
           // { }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            button2.Visible = false;
            button3.Visible = true;
            button4.Visible = false;
            MyVideo = new Thread(new ThreadStart(StartVideo));
            MyVideo.IsBackground = true;
            MyVideo.Start();     
        }
        public void StartVideo()
        {
            myCamera = new TouchlessMgr();
            myCamera.CurrentCamera = myCamera.Cameras[0];
            myCamera.Cameras[0].OnImageCaptured += new EventHandler<CameraEventArgs>(OnImageCaptured);
        }
        public void OnImageCaptured(object sender, CameraEventArgs e)
        {
            Invoke(new MethodInvoker(delegate() { UpdatePictureBox(e); }));
        }
        public void UpdatePictureBox(CameraEventArgs e)
        {
            pictureBox1.Image = e.Image;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            myCamera.Dispose();
            myCamera = null; 
            button2.Visible = true;
            button3.Visible = false;
            button4.Visible = false;
             
        }

        private void button5_Click(object sender, EventArgs e)
        {
            
        }
        private void Call()
        {
            try
            {
                //Get the IP we want to call.
                otherPartyIP = new IPEndPoint(IPAddress.Parse(textBox3.Text), 1450);
                otherPartyEP = (EndPoint)otherPartyIP;
                vocoder = Vocoder.ALaw;
                //Send an invite message.
                SendMessage(Command.Invite, otherPartyEP);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-Call ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendMessage(Command cmd, EndPoint sendToEP)
        {
            try
            {
                //Create the message to send.
                Data msgToSend = new Data();

                msgToSend.strName = textBox4.Text;   //Name of the user.
                msgToSend.cmdCommand = cmd;         //Message to send.
                msgToSend.vocoder = vocoder;        //Vocoder to be used.

                byte[] message = msgToSend.ToByte();

                //Send the message asynchronously.
                clientSocket.BeginSendTo(message, 0, message.Length, SocketFlags.None, sendToEP, new AsyncCallback(OnSend), null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-SendMessage ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void OnSend(IAsyncResult ar)
        {
            try
            {
                clientSocket.EndSendTo(ar);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-OnSend ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void OnReceive(IAsyncResult ar)
        {
            try
            {
                EndPoint receivedFromEP = new IPEndPoint(IPAddress.Any, 0);

                //Get the IP from where we got a message.
                clientSocket.EndReceiveFrom(ar, ref receivedFromEP);

                //Convert the bytes received into an object of type Data.
                Data msgReceived = new Data(byteData);

                //Act according to the received message.
                switch (msgReceived.cmdCommand)
                {
                    //We have an incoming call.
                    case Command.Invite:
                        {
                            if (bIsCallActive == false)
                            {
                                //We have no active call.

                                //Ask the user to accept the call or not.
                                if (MessageBox.Show("The Call is coming from " + msgReceived.strName + ".\r\n\r\nAccept it?",
                                    "VoiceChat", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                {
                                    SendMessage(Command.OK, receivedFromEP);
                                    vocoder = msgReceived.vocoder;
                                    otherPartyEP = receivedFromEP;
                                    otherPartyIP = (IPEndPoint)receivedFromEP;
                                    InitializeCall();
                                    ReadImg = new Thread(new ThreadStart(Start_Receiving_Video_Conference));
                                    ReadImg.Start();
                                    videocall();
                                }
                                else
                                {
                                    //The call is declined. Send a busy response.
                                    SendMessage(Command.Busy, receivedFromEP);
                                }
                            }
                            else
                            {
                                //We already have an existing call. Send a busy response.
                                SendMessage(Command.Busy, receivedFromEP);
                            }
                            break;
                        }

                    //OK is received in response to an Invite.
                    case Command.OK:
                        {
                            //Start a call.
                            ReadImg = new Thread(new ThreadStart(Start_Receiving_Video_Conference));
                            ReadImg.Start();  
                            InitializeCall();
                           videocall();
                          // ImageTopic.Start();
                            break;
                        }

                    //Remote party is busy.
                    case Command.Busy:
                        {
                            MessageBox.Show("User busy.", "VoiceChat", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                            break;
                        }

                    case Command.Bye:
                        {
                            //Check if the Bye command has indeed come from the user/IP with which we have
                            //a call established. This is used to prevent other users from sending a Bye, which
                            //would otherwise end the call.
                            if (receivedFromEP.Equals(otherPartyEP) == true)
                            {
                                //End the call.
                                UninitializeCall();
                            }
                            break;
                        }
                }

                byteData = new byte[1024];
                //Get ready to receive more commands.
                clientSocket.BeginReceiveFrom(byteData, 0, byteData.Length, SocketFlags.None, ref receivedFromEP, new AsyncCallback(OnReceive), null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-OnReceive ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeCall()
        {
            try
            {
                //Start listening on port 1500.
                udpClient = new UdpClient(1550);

                Thread senderThread = new Thread(new ThreadStart(Send));
                Thread receiverThread = new Thread(new ThreadStart(Receive));
                bIsCallActive = true;

                //Start the receiver and sender thread.
                receiverThread.Start();
                senderThread.Start();
                Invoke(new MethodInvoker(crossthread1));
               
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-InitializeCall ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        public void crossthread1()
        {
           // button5.Enabled = false;
            button6.Enabled = true;

        }

        private void UninitializeCall()
        {
            //Set the flag to end the Send and Receive threads.
            bStop = true;

            bIsCallActive = false;
            Invoke(new MethodInvoker(crossthread2));
           
        }
        public void crossthread2()
        {
           // button5.Enabled = true;
            button6.Enabled = false;
        }

        private void Send()
        {
            try
            {
                //The following source code get speech from microphone and then send them 
                //across network.

                captureBuffer = new CaptureBuffer(captureBufferDescription, capture);

                CreateNotifyPositions();

                int halfBuffer = bufferSize / 2;

                captureBuffer.Start(true);

                bool readFirstBufferPart = true;
                int offset = 0;

                MemoryStream memStream = new MemoryStream(halfBuffer);
                bStop = false;
                while (!bStop)
                {
                    autoResetEvent.WaitOne();
                    memStream.Seek(0, SeekOrigin.Begin);
                    captureBuffer.Read(offset, memStream, halfBuffer, LockFlag.None);
                    readFirstBufferPart = !readFirstBufferPart;
                    offset = readFirstBufferPart ? 0 : halfBuffer;

                    
                    if (vocoder == Vocoder.ALaw)
                    {
                        byte[] dataToWrite = ALawEncoder.ALawEncode(memStream.GetBuffer());
                        udpClient.Send(dataToWrite, dataToWrite.Length, otherPartyIP.Address.ToString(), 1550);
                    }

                    else
                    {
                        byte[] dataToWrite = memStream.GetBuffer();
                        udpClient.Send(dataToWrite, dataToWrite.Length, otherPartyIP.Address.ToString(), 1550);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-Send ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                captureBuffer.Stop();

                //Increment flag by one.
                nUdpClientFlag += 1;

                //When flag is two then it means we have got out of loops in Send and Receive.
                while (nUdpClientFlag != 2)
                { }

                //Clear the flag.
                nUdpClientFlag = 0;

                //Close the socket.
                udpClient.Close();
            }
        }

        private void CreateNotifyPositions()
        {
            try
            {
                autoResetEvent = new AutoResetEvent(false);
                notify = new Notify(captureBuffer);
                BufferPositionNotify bufferPositionNotify1 = new BufferPositionNotify();
                bufferPositionNotify1.Offset = bufferSize / 2 - 1;
                bufferPositionNotify1.EventNotifyHandle = autoResetEvent.SafeWaitHandle.DangerousGetHandle();
                BufferPositionNotify bufferPositionNotify2 = new BufferPositionNotify();
                bufferPositionNotify2.Offset = bufferSize - 1;
                bufferPositionNotify2.EventNotifyHandle = autoResetEvent.SafeWaitHandle.DangerousGetHandle();

                notify.SetNotificationPositions(new BufferPositionNotify[] { bufferPositionNotify1, bufferPositionNotify2 });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-CreateNotifyPositions ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        
          //Receive audio data coming on port 1550 and feed it to the speakers to be played.
        
        private void Receive()
        {
            try
            {
                bStop = false;
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                while (!bStop)
                {
                    //Receive data.
                    byte[] byteData = udpClient.Receive(ref remoteEP);

                    //G711 compresses the data by 50%, so we allocate a buffer of double
                    //the size to store the decompressed data.
                    byte[] byteDecodedData = new byte[byteData.Length * 2];

                    //Decompress data using the proper vocoder.
                    if (vocoder == Vocoder.ALaw)
                    {
                        ALawDecoder.ALawDecode(byteData, out byteDecodedData);
                    }

                    else
                    {
                        byteDecodedData = new byte[byteData.Length];
                        byteDecodedData = byteData;
                    }


                    //Play the data received to the user.
                    playbackBuffer = new SecondaryBuffer(playbackBufferDescription, device);
                    playbackBuffer.Write(0, byteDecodedData, LockFlag.None);
                    playbackBuffer.Play(0, BufferPlayFlags.Default);
                }
            }
            catch (Exception ex)
            {
               
            }
            finally
            {
                nUdpClientFlag += 1;
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            DropCall();
        }
        private void DropCall()
        {
            try
            {
                //Send a Bye message to the user to end the call.
                SendMessage(Command.Bye, otherPartyEP);
                UninitializeCall();
            }
            catch (Exception ex)
            {
                
            }
        }
        class Data
        {
            //Default constructor.
            public Data()
            {
                this.cmdCommand = Command.Null;
                this.strName = null;
                vocoder = Vocoder.ALaw;
            }

            //Converts the bytes into an object of type Data.
            public Data(byte[] data)
            {
                //The first four bytes are for the Command.
                this.cmdCommand = (Command)BitConverter.ToInt32(data, 0);

                //The next four store the length of the name.
                int nameLen = BitConverter.ToInt32(data, 4);

                //This check makes sure that strName has been passed in the array of bytes.
                if (nameLen > 0)
                    this.strName = Encoding.UTF8.GetString(data, 8, nameLen);
                else
                    this.strName = null;
            }

            //Converts the Data structure into an array of bytes.
            public byte[] ToByte()
            {
                List<byte> result = new List<byte>();

                //First four are for the Command.
                result.AddRange(BitConverter.GetBytes((int)cmdCommand));

                //Add the length of the name.
                if (strName != null)
                    result.AddRange(BitConverter.GetBytes(strName.Length));
                else
                    result.AddRange(BitConverter.GetBytes(0));

                //Add the name.
                if (strName != null)
                    result.AddRange(Encoding.UTF8.GetBytes(strName));

                return result.ToArray();
            }

            public string strName;      
            public Command cmdCommand;  
            public Vocoder vocoder;
        }






       

        
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using TouchlessLib;
using MSR.LST;
using MSR.LST.Net.Rtp;
using SharpFFmpeg;
using Microsoft.DirectX.DirectSound;
using g711audio;
 

namespace WindowsFormsApplication4
{
    
    public partial class Form1 : Form
    {

        public static String mess = " \0", line = "\0 ";
        public static TcpListener t1,tcplis;
        public static TcpClient tc,tcpclient;
        public static NetworkStream ns,netstr;
        public static StreamWriter sw;
        public static StreamReader sr;
        //voice  datamembers 
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
        private IPEndPoint otherPartyIP;            //The receiver ip address 
        private EndPoint otherPartyEP;
        private volatile bool bIsCallActive;                 //To check whether the call is active.
        private Vocoder vocoder;
        private byte[] byteData = new byte[1024];   //Buffer which stores the data which is received.
        private volatile int nUdpClientFlag;                 
        public Thread VoiceThread;
        // voice variables end
        Thread TextChat;
        Thread Read;
       
        //public Thread TextChat;
        public Thread VideoChat;
        public Thread MyVideo;
        TouchlessMgr availableCamera = null; 
        TouchlessMgr myCamera = null;
        
        //capture video variables
        [DllImport("user32.dll")]
        static  extern bool AnimateWindow(IntPtr hWnd, int time, AnimateWindowFlags flags);
        // video chat variables
        public static IPEndPoint ep;
        public RtpSession rtpSession;
        public RtpSender rtpSender;
        public  MemoryStream ys;
        Thread WriteImage;
        Socket sck;
        IPEndPoint ipep;

        enum AnimateWindowFlags
        {
            
            AW_VER_POSITIVE = 0x00000004,
            AW_HIDE = 0x00010000, 
            AW_BLEND = 0x00080000
        };
        enum Command
        {
            Invite, //Make a call to the client .
            Bye,    //Ending an active call.
            Busy,   //It indicates the User is busy.
            OK,     //Response to an invite message. OK just indicates that the call is accepted.
            Null,   //No any command.
        };

        //Vocoder
        enum Vocoder
        {
            ALaw,   //A-Law vocoder.
            None,   //Don't use any vocoder.
        };

        public Form1()
        {
            InitializeComponent();
            t1 = new TcpListener(IPAddress.Parse("192.168.1.10"), 4000);
            
            TextChat = new Thread(new ThreadStart(ClientConnection));            
            TextChat.IsBackground = true;
           
            t1.Start();
            
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

                short channels = 1; //Stereo.
                short bitsPerSample = 16; //we can either use 8 or 16.
                int samplesPerSecond = 22050; //11KHz will use 11025 , 22KHz will use 22050, 44KHz use 44100 etc.

                //The waveformat which has to be captured is set here.
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
                //Here we use technique to receive data asynchronously.
                clientSocket.BeginReceiveFrom(byteData,
                                           0, byteData.Length,
                                           SocketFlags.None,
                                           ref remoteEP,
                                           new AsyncCallback(OnReceive),
                                           null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "dangerVoiceChat-Initialize ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }        
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            button3.Visible = false;
            button4.Visible = false;
            AnimateWindow(this.Handle, 3000, AnimateWindowFlags.AW_VER_POSITIVE);
        }
        private void button1_Click(object sender, EventArgs e)
        {
            if(button1.Text.Equals("Disconnect"))
            {
                availableCamera.Dispose();
                availableCamera = null;
                button1.Text = "Connect";
                button1.Visible = true;
                button1.Enabled = false;
                textBox3.Enabled = true;
                button2.Enabled = false;
                Cleanup();
            }

            
                
            if (!t1.Pending())
            {
                textBox1.AppendText("\n Client is offline please try again later \n");
                
            }
            else
            {
                button2.Visible = false;
                textBox1.AppendText("\n Connected successfully............");
                tc = t1.AcceptTcpClient();
                this.textBox2.KeyPress += new System.Windows.Forms.KeyPressEventHandler(CheckKeys);
                TextChat.Start();
                ep = new IPEndPoint(IPAddress.Parse(textBox3.Text), 5001);
                if (button1.Text.Equals("Connect"))
                {
                    HookRtpEvents();
                    JoinRtpSession(Dns.GetHostName());
                    button1.Text = "Disconnect";
                    textBox3.Enabled = false;
                    button4.Visible = true;

                }
            }             
        }
        
           
        // Hook up the rtp events to use real time transport protocol 
        
        private void HookRtpEvents()
        {
            RtpEvents.RtpParticipantAdded += new RtpEvents.RtpParticipantAddedEventHandler(RtpParticipantAdded);
            RtpEvents.RtpParticipantRemoved += new RtpEvents.RtpParticipantRemovedEventHandler(RtpParticipantRemoved);
            RtpEvents.RtpStreamAdded += new RtpEvents.RtpStreamAddedEventHandler(RtpStreamAdded);
            RtpEvents.RtpStreamRemoved += new RtpEvents.RtpStreamRemovedEventHandler(RtpStreamRemoved);           
        }
        private void JoinRtpSession(string name)
        {
            rtpSession = new RtpSession(ep, new RtpParticipant(name, name), true, true); // creating the Rtp session
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

        // whenever we receive the frame we have to decode and display as show below.
        private void FrameReceived(object sender, RtpStream.FrameReceivedEventArgs ea)
        {
           
            System.IO.MemoryStream ms = new MemoryStream(ea.Frame.Buffer);
            IFFmpeg.avcodec_find_decoder(IFFmpeg.CodecID.CODEC_ID_H263);
            IFFmpeg.DecodeFrame(Image.FromStream(ms), native);
            IFFmpeg.ConvertYUV2RGB(yuv, Image.FromStream(ms));
            pictureBox2.Image = Image.FromStream(ms);
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
        private void CheckKeys(object sender, System.Windows.Forms.KeyPressEventArgs e) // keypress event monitors key pressed on keyboard
        {
            if (e.KeyChar == (char)13)  //The ascii value of enter key is 13
            {
                mess = textBox2.Text;
                textBox1.AppendText("\nServer:" + mess.Trim() + "\n");
                sw.WriteLine("Server:{0}", mess.Trim());
                sw.Flush();
                textBox2.Clear();
                if (mess.StartsWith("bye"))
                {
                    sw.Close();
                    ns.Close();
                    tc.Close();
                    t1.Stop();
                }

            }
        }

        private void ReadMessage()
        {
            try
            {
                while (true)
                {
                    line = sr.ReadLine();
                    Invoke(new MethodInvoker(AppendLine));
                    if (line.StartsWith("bye"))
                        break;
                }
                sr.Close();
                ns.Close();
                tc.Close();
                t1.Stop();
            }
            catch
            {
            }
        }

        public void AppendLine()
        {
            textBox1.AppendText(line+"\n");
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
            
        private void button4_Click(object sender, EventArgs e)
        {
            VoiceThread = new Thread(new ThreadStart(Call));
            VoiceThread.IsBackground = true;
            VoiceThread.Start();            
        }
        public void VideoCall()
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
     //   int [] yuv1;
      // byte [] native1;
        public void SendCapturedVideo(CameraEventArgs e)
        {
           /* this is the source code to compress the video frame and 
            * send it using real time transport protocol */
         
        /*  pictureBox1.Image = e.Image;
             try
              {
               ys = new MemoryStream();// Store it in Binary Array as Stream
               Image bmap;

                bmap=e.Image;  
                IFFmpeg.avcodec_find_encoder(IFFmpeg.CodecID.CODEC_ID_H263);
                IFFmpeg.ConvertRGB2YUV(e.Image, yuv1);
                IFFmpeg.EncodeFrame(yuv1, native1);
                
                bmap.Save(ys, ImageFormat.Jpeg);                
                 rtpSender.Send(ys.ToArray());
             
           }
            catch (Exception) 
            {  }   */


            // the below is the source code for sending using user datagram protocol. both rtp and udp are tested
            MemoryStream msc = new MemoryStream();
            sck = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            ipep = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 6000);
            msc = new MemoryStream();// Store it in Binary Array as Stream
            Image bmap;
            bmap = e.Image;
            bmap.Save(msc, ImageFormat.Jpeg);
            byte[] arrImage = msc.GetBuffer();
            msc.Flush();
            msc.Close();
            sck.SendTo(arrImage, ipep);
            sck.Close();
            pictureBox1.Image = e.Image;    
        }
        
       
        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }
         
        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void button3_Click_1(object sender, EventArgs e)
        {
            button2.Visible = true;
            button3.Visible = false;
            button4.Visible = false;
            myCamera.Dispose();
            myCamera = null;  
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

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
                //Sendmessage is used to send an invite message.
                SendMessage(Command.Invite, otherPartyEP);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "oopsVoiceChat-Call ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                                if (MessageBox.Show("Call coming from " + msgReceived.strName + ".\r\n\r\nAccept it?",
                                    "VoiceChat", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                {
                                    SendMessage(Command.OK, receivedFromEP);
                                    vocoder = msgReceived.vocoder;
                                    otherPartyEP = receivedFromEP;
                                    otherPartyIP = (IPEndPoint)receivedFromEP;
                                    InitializeCall();
                                    VideoCall();
                                }
                                else
                                {
                                    //Now when The call is declined we Send a busy response to the client.
                                    SendMessage(Command.Busy, receivedFromEP);
                                }
                            }
                            else
                            {
                                //Even if We already have an existing call then also we  Send a busy response.
                                SendMessage(Command.Busy, receivedFromEP);
                            }
                            break;
                        }

                    //OK is received in response to an Invite.
                    case Command.OK:
                        {
                            Thread init = new Thread(new ThreadStart(InitializeCall));
                            init.IsBackground = true;
                            init.Start();
                            VideoCall();
                           // WriteImage.Start();
                          //  Thread testthread = new Thread(new ThreadStart(VideoCall));
                           // testthread.IsBackground = true;
                           // testthread.Start();
                            
                           // VideoCall();
                            break;
                        }

                    //The destination is busy.
                    case Command.Busy:
                        {
                            MessageBox.Show("User busy.", "VoiceChat", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                            break;
                        }

                    case Command.Bye:
                        {
                            
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
                senderThread.IsBackground = true;
                receiverThread.IsBackground = true;
                bIsCallActive = true;

                //Start the receiver and sender thread.
                receiverThread.Start();
                senderThread.Start();
                //VideoCall();
                Invoke(new MethodInvoker(CROSSTHREAD1));
                
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-InitializeCall ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void CROSSTHREAD1()
        {
          //  button5.Enabled = false;
            button6.Enabled = true;
        }

        private void UninitializeCall()
        {
            //Set the flag to end the Send and Receive threads.
            bStop = true;

            bIsCallActive = false;
            Invoke(new MethodInvoker(CROSSTHREAD2));
            
        }

        public void CROSSTHREAD2()
        {
           // button5.Enabled = true;
            button6.Enabled = false;
        }
        private void Send()
        {
            try
            {
                //The following lines captures the speech from the  microphone and then send it on to the network. 
                 
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
           // Voice compression takes place here
                    if (vocoder == Vocoder.ALaw)
                    {
                        byte[] dataToWrite = ALawEncoder.ALawEncode(memStream.GetBuffer());
                        udpClient.Send(dataToWrite, dataToWrite.Length, otherPartyIP.Address.ToString(), 1550);
                        // we can use either rtp or udp to send the data.
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
                
                nUdpClientFlag = 0;
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

                    //G711 speech codec  compresses the data by 50%, so we allocate a buffer of double
                    //the size which is required to  store the decompressed data.
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
                MessageBox.Show(ex.Message, "VoiceChat-Receive ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                //We Send a Bye message to the user in order to end the call.
                SendMessage(Command.Bye, otherPartyEP);
                UninitializeCall();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VoiceChat-DropCall ()", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                //These first four bytes are used for the Command.
                this.cmdCommand = (Command)BitConverter.ToInt32(data, 0);

                //These next four bytes are used to store the length of the name.
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

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (bIsCallActive)
            {
                UninitializeCall();
                DropCall();
                clientSocket.Close();
            }
          
            try
            {
             
            }
            catch (Exception) { }
            AnimateWindow(this.Handle, 3000, AnimateWindowFlags.AW_BLEND | AnimateWindowFlags.AW_HIDE);
            
            Application.Exit();
        }
       
            
    }
}

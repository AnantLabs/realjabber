#define ENABLE_INTEROP // Remove this if Interop not supported (i.e. Mono, Mac, Linux)

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using jabber.client;
using jabber.protocol.client;
using System.Diagnostics;
using RealJabber.RealTimeTextUtil;

#if ENABLE_INTEROP
// Will only work on Windows, not in MONO (Mac/Linux)
using System.Runtime.InteropServices;
#endif

/// <summary>
/// This is an experimental demonstration XMPP Chat client that implements a new XMPP extension:
/// XMPP In-Band Real Time Text - Version 0.0.3 - http://www.realjabber.org
/// Written by Mark D. Rejhon - mailto:markybox@gmail.com - http://www.marky.com/resume
/// 
/// COPYRIGHT
/// Copyright 2011 by Mark D. Rejhon - Rejhon Technologies Inc.
/// 
/// LICENSE
/// Licensed under the Apache License, Version 2.0 (the "License");
/// you may not use this file except in compliance with the License.
/// You may obtain a copy of the License at
///     http://www.apache.org/licenses/LICENSE-2.0
/// Unless required by applicable law or agreed to in writing, software
/// distributed under the License is distributed on an "AS IS" BASIS,
/// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
/// See the License for the specific language governing permissions and
/// limitations under the License.
///
/// NOTES
/// Formerly based on the public-domain C# .NET Google Chat client at CodeProject
/// http://www.codeproject.com/KB/gadgets/googletalk.aspx
/// 
/// IMPORTANT NOTICE
/// Mark Rejhon retains copyright to the use of the name "RealJabber".
/// Modified versions of this software not released by Mark Rejhon must be released under a 
/// different name than "RealJabber".
/// </summary>
namespace RealJabber
{
    /// <summary>Display of a chat window</summary>
    public partial class FrmChat : Form
    {
        private const string CHAT_LINE_FORMAT = "{0} says: ";
        private const string CHAT_LINE_REALTIME = "{0} (typing): ";
        private const float TEXTSIZE_SMALL = 8.25f;
        private const float TEXTSIZE_MEDIUM = 14.25f;
        private const float TEXTSIZE_LARGE = 18.25f;
        private const float TEXTSIZE_HUGE = 24.25f;

        // Unicode 0x2503 is a thick bar character. Use 0 to hide cursor. Alternative is to use common '|' pipe character
        private const char CURSOR_CHAR = (char)(0x2503);

        private Color rttTextColor = Color.Blue;
        private char cursorChar = CURSOR_CHAR;
        private delegate void RefreshCallback();
        private RefreshCallback delegateRefresh;
        private ChatSession m_chat = new ChatSession();
        private bool rttEnabled = true;
        private bool ignoreChangeEvent = false;
        private bool enableChatStates = false;
        private RichTextBox lastSendBox = null;
        private RealTimeText.Encoder encoderRTT;
        private RichTextBox tempRTF = new RichTextBox();
        private RichTextBox cachedRTF1 = new RichTextBox();
        private RichTextBox cachedRTF2 = new RichTextBox();

        private Object convoLock = new Object();
        private bool convoUpdated = true;
        private Timer convoUpdateTimer = new Timer();

        /// <summary>Constructor</summary>
        public FrmChat()
        {
            InitializeComponent();
            timerRTTsend.Tick += new EventHandler(timerRTTsend_Tick);
            delegateRefresh = new RefreshCallback(UpdateConversationWindow);
            convoUpdateTimer.Tick += new EventHandler(convoUpdateTimer_Tick);
            lastSendBox = textboxSendMsg1;
        }

        /// <summary>The JID we are chatting to in this window</summary>
        public string Nickname
        {
            get { return _nickname; }
            set { _nickname = value; }
        }
        private string _nickname;

        /// <summary>The JID we are chatting to in this window</summary>
        public jabber.JID JID
        {
            get { return _jid; }
            set { _jid = value; }
        }
        private string _jid;

        /// <summary>The XMPP library to communicate through</summary>
        public JabberClient JabberObject
        {
            get { return _jabberClient; }
            set { _jabberClient = value; }
        }
        private JabberClient _jabberClient;

        /// <summary>A flag indicating whether a message has been received already</summary>        
        public bool ReceiveFlag
        {
            get { return _receiveFlag; }
            set { _receiveFlag = value; }
        }
        private bool _receiveFlag;

        /// <summary>Form load event -- this window was just created</summary>
        private void FrmChat_Load(object sender, EventArgs e)
        {
            encoderRTT = new RealTimeText.Encoder(_jabberClient.Document, true);
            groupBoxParticipant1.Text = JID.Bare;
            groupBoxParticipantLocal.Text = _jabberClient.User;
            SendBox.Focus();
        }

        /// <summary>Window activation event</summary>
        private void FrmChat_Activated(object sender, EventArgs e)
        {
            this.BringToFront();
            SendBox.Focus();
            UpdateMenuCheckmarks();
        }

        /// <summary>Append a line of chat to conversation.  Also clears the real time text</summary>
        public void AppendConversation(jabber.JID from, string str, Color color)
        {
            m_chat.RemoveRealTimeMessage(from);
            ChatMessage line = new ChatMessage(from, str, color);
            m_chat.History.Add(line);
            ResetConversationMessageHistoryCache();
            UpdateConversationWindow();
        }

        /// <summary>Process the contents of an XMPP message</summary>
        public void HandleMessage(jabber.protocol.client.Message msg)
        {
            //Console.WriteLine(msg.OuterXml.ToString());
            if (rttEnabled)
            {
                // Did we receive a <rtt> element?
                XmlElement rttBlock = (XmlElement)msg["rtt"];
                if (rttBlock != null)
                {
                    // Create a new real time mesage if not already created
                    RealTimeMessage rttMessage = m_chat.GetRealTimeMessage(msg.From.Bare);
                    if (rttMessage == null)
                    {
                        rttMessage = m_chat.NewRealTimeMessage(_jabberClient.Document, (string)msg.From.Bare, Color.DarkBlue);
                        rttMessage.Decoder.TextUpdated += new RealTimeText.Decoder.TextUpdatedHandler(decoder_TextDecoded);
                        rttMessage.Decoder.SyncStateChanged += new RealTimeText.Decoder.SyncStateChangedHandler(decoder_SyncStateChanged);
                    }
                    // Process <rtt> element
                    rttMessage.Decoder.Decode(rttBlock);

                    // Highlight frozen text differently (stalled due to missing message)
                    rttMessage.Color = rttMessage.Decoder.InSync ? Color.DarkBlue : Color.LightGray;

                    // If key press intervals are disabled, decode immediately
                    if (!encoderRTT.KeyIntervalsEnabled)
                    {
                        rttMessage.Decoder.FullDecodeNow();
                        UpdateConversationWindow();
                    }
                }
            }
           
            if (msg.Body != null)
            {
                // Completed line of text
                AppendConversation(msg.From.Bare, msg.Body, Color.DarkBlue);
            }
        }
                              
        /// <summary>Event that is called when the RTT decoder decodes the next part of real time text</summary>
        void decoder_TextDecoded(RealTimeText.Decoder decoder)
        {
            if (this.Visible)
            {
                lock (convoLock)
                {
                    if (convoUpdated)
                    {
                        convoUpdated = false;
                        this.BeginInvoke(delegateRefresh);
                    }
                    else
                    {
                        convoUpdateTimer.Interval = 10;
                        convoUpdateTimer.Start();
                    }
                }
            }
        }

        /// <summary>Event that is called when RTT goes in or out of sync</summary>
        void decoder_SyncStateChanged(RealTimeText.Decoder decoder, bool inSync)
        {
            if (this.Visible)
            {
                // Refresh window through decoder
                decoder_TextDecoded(decoder);
            }
        }

        /// <summary>Conversation update timer</summary>
        void convoUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (convoUpdated)
            {
                convoUpdateTimer.Stop();
                this.BeginInvoke(delegateRefresh);
            }
        }

        /// <summary>Transmit RTT elements if needed.</summary>
        private bool JabberSendRTT()
        {
            bool rttSent = false;
            try
            {
                if (!rttEnabled) return false;

                if (!encoderRTT.IsEmpty)
                {
                    // Create the XMPP <message> with <rtt> updates, and transmit.
                    jabber.protocol.client.Message reply = new jabber.protocol.client.Message(_jabberClient.Document);
                    reply.To = JID.Bare;
                    reply.AppendChild(encoderRTT.GetEncodedRTT());
                    if (enableChatStates)
                    {
                        reply.AppendChild(reply.OwnerDocument.CreateElement("composing")); //, "http://jabber.org/protocol/chatstates"));
                    }
                    _jabberClient.Write(reply);
                    rttSent = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("!!! Exception in JabberSendMessage(): " + ex.ToString());
            }
            return rttSent;
        }

        /// <summary>Transmit a message. Either a regular message, or real time text update, or both.</summary>
        private void JabberSendMessage(string body)
        {
            try
            {
                if (body == null) return;

                // Create the XMPP <message>
                jabber.protocol.client.Message reply = new jabber.protocol.client.Message(_jabberClient.Document);
                reply.To = JID.Bare;
                
                // Add <rtt> element if needed (flush final RTT element with full message delivery)
                if (rttEnabled)
                {
                    encoderRTT.Encode(SendBox.Text, SendBox.SelectionStart);
                    if (!encoderRTT.IsEmpty) reply.AppendChild(encoderRTT.GetEncodedRTT());
                    encoderRTT.NextMsg();
                }

                // Add the <body> element
                reply.Body = body;
                if (enableChatStates)
                {
                    reply.AppendChild(reply.OwnerDocument.CreateElement("active")); //, "http://jabber.org/protocol/chatstates"));
                }
                _jabberClient.Write(reply);
            }
            catch (Exception ex)
            {
                Console.WriteLine("!!! Exception in JabberSendMessage(): " + ex.ToString());
            }
        }

        /// <summary>Keypress event in the Send Message text box.  
        /// Monitor for Enter keypresses for sending messages.
        /// If Shift+Enter is done, don't transmit, and just treat it as an embedded newline in the message</summary>
        private void textboxSendMsg_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Return) && 
                (0 == (e.Modifiers & Keys.Shift)))
            {
                if (SendBox.Text.Length > 0)
                {
                    timerRTTsend.Stop();

                    // Send message
                    string sentText = SendBox.Text;
                    SendBox.Text = "";
                    JabberSendMessage(sentText);

                    // Display sent message
                    AppendConversation(_jabberClient.User, sentText, Color.DarkRed);
                    UpdateConversationWindow();

                    SendBox.Focus();
                    this.ReceiveFlag = false;
                }
                e.Handled = true;
            }
        }

        /// <summary>Text changed.</summary>
        private void textboxSendMsg_TextChanged(object sender, EventArgs e)
        {
            if (ignoreChangeEvent) return;
            if (SendBox == lastSendBox) TextOrCursorPosChanged();
        }

        /// <summary>Cursor moved.</summary>
        private void textboxSendMsg_SelectionChanged(object sender, EventArgs e)
        {
            if (ignoreChangeEvent) return;
            if (SendBox == lastSendBox) TextOrCursorPosChanged();
        }

        /// <summary>Either text or cursor position changed</summary>
        private void TextOrCursorPosChanged()
        {
            if (rttEnabled)
            {
                if (encoderRTT.KeyIntervalsEnabled)
                {
                    encoderRTT.Encode(SendBox.Text, SendBox.SelectionStart);
                }
                if (!timerRTTsend.Enabled && (SendBox.Parent != null))
                {
                    timerRTTsend.Interval = (encoderRTT.TransmitInterval > 0) ? encoderRTT.TransmitInterval : 1;
                    timerRTTsend.Start();
                }
            }
        }

        /// <summary>Real Time Text transmission timer</summary>
        private void timerRTTsend_Tick(object sender, EventArgs e)
        {
            if (rttEnabled)
            {
                if (!encoderRTT.KeyIntervalsEnabled)
                {
                    encoderRTT.Encode(SendBox.Text, SendBox.SelectionStart);
                }
            }
            if (!JabberSendRTT())
            {
                timerRTTsend.Stop();
            }
        }

        /// <summary>Visual presentation of the conversation buffer and real-time-text</summary>
        private void UpdateConversationWindow()
        {
            // Render the conversation depending on which tab is selected.
            switch (tabControl.SelectedTab.Tag.ToString())
            {
                case "NORMAL": // Tab: Normal IM view
                    tempRTF.Clear();
                    if (cachedRTF1.Text.Length == 0)
                    {
                        m_chat.FormatAllLinesRTF(cachedRTF1, CHAT_LINE_FORMAT, null);
                    }
                    tempRTF.Rtf = cachedRTF1.Rtf;
                    m_chat.FormatAllRealTimeRTF(tempRTF, CHAT_LINE_REALTIME, null, cursorChar, rttTextColor);
                    textboxConversation1.Rtf = tempRTF.Rtf;
                    ScrollToBottom(textboxConversation1);
                    break;

                case "HYBRID": // Tab: Hybrid IM view
                    tempRTF.Clear();
                    if (cachedRTF1.Text.Length == 0)
                    {
                        m_chat.FormatAllLinesRTF(cachedRTF1, CHAT_LINE_FORMAT, null);
                    }
                    tempRTF.Rtf = cachedRTF1.Rtf;
                    textboxConversation2.Rtf = tempRTF.Rtf;
                    ScrollToBottom(textboxConversation2);
                    tempRTF.Clear();
                    m_chat.FormatAllRealTimeRTF(tempRTF, CHAT_LINE_REALTIME, null, cursorChar, rttTextColor);
                    textboxRealTime.Rtf = tempRTF.Rtf;
                    ScrollToTop(textboxRealTime);
                    break;

                case "SPLIT": // Tab: Splitscreen Chat view
                    tempRTF.Clear();
                    if (cachedRTF1.Text.Length == 0)
                    {
                        m_chat.FormatAllLinesRTF(cachedRTF1, "", this.JID.Bare);
                    }
                    tempRTF.Rtf = cachedRTF1.Rtf;
                    m_chat.FormatAllRealTimeRTF(tempRTF, "", this.JID.Bare, cursorChar, rttTextColor);
                    textboxParticipant1.Rtf = tempRTF.Rtf;
                    ScrollToBottom(textboxParticipant1);
                    tempRTF.Clear();
                    if (cachedRTF2.Text.Length == 0)
                    {
                        m_chat.FormatAllLinesRTF(cachedRTF2, "", _jabberClient.User);
                    }
                    tempRTF.Rtf = cachedRTF2.Rtf;
                    if (tempRTF.Text.Length > 0)
                    {
                        tempRTF.Select(tempRTF.Text.Length - 1, 1);
                        tempRTF.SelectedText = "";
                    }
                    textboxParticipantLocal.Rtf = tempRTF.Rtf;
                    ScrollToBottom(textboxParticipantLocal);
                    break;
            }
            lock (convoLock)
            {
                convoUpdated = true;
            }
        }

        /// <summary>Forces reinitialization of conversation window message history</summary>
        private void ResetConversationMessageHistoryCache()
        {
            cachedRTF1.Clear();
            cachedRTF2.Clear();
        }

        /// <summary>Copies contents and cursor position from one textbox to another</summary>
        /// <param name="oldTextBox">source text box</param>
        /// <param name="newTextBox">destination text box</param>
        private void CopyTextBox(RichTextBox oldTextBox, RichTextBox newTextBox)
        {
            if (oldTextBox == newTextBox) return;
            ignoreChangeEvent = true;
            newTextBox.Text = oldTextBox.Text;
            newTextBox.SelectionStart = oldTextBox.SelectionStart;
            newTextBox.SelectionLength = oldTextBox.SelectionLength;
            ignoreChangeEvent = false;
        }

        /// <summary>Switching tabs (switch between different views of chat)</summary>
        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ResetConversationMessageHistoryCache();
            CopyTextBox(lastSendBox, textboxSendMsg1);
            CopyTextBox(lastSendBox, textboxSendMsg2);
            CopyTextBox(lastSendBox, textboxSendMsg3);
            UpdateConversationWindow();
            lastSendBox = SendBox;
        }

        // Reset the cursor to the send message box upon any repeated clicks on any tab
        private void tabControl_MouseClick(object sender, MouseEventArgs e)
        {
            SendBox.Focus();
        }

        // Reset the cursor to the send message box if the user attempts to start
        // typing while the cursor is not focussed in the send message box.
        private void textboxParticipantLocal_KeyPress(object sender, KeyPressEventArgs e)
        {
            SendBox.Focus();
        }
        private void textboxParticipant1_KeyPress(object sender, KeyPressEventArgs e)
        {
            SendBox.Focus();
        }
        private void textboxConversation1_KeyPress(object sender, KeyPressEventArgs e)
        {
            SendBox.Focus();
        }
        private void textboxConversation2_KeyPress(object sender, KeyPressEventArgs e)
        {
            SendBox.Focus();
        }
        private void textboxRealTime_KeyPress(object sender, KeyPressEventArgs e)
        {
            SendBox.Focus();
        }

        private RichTextBox SendBox
        {
            get
            {
                if (tabControl.SelectedTab == tabPage1) return textboxSendMsg1;
                if (tabControl.SelectedTab == tabPage2) return textboxSendMsg2;
                if (tabControl.SelectedTab == tabPage3) return textboxSendMsg3;
                return textboxSendMsg1;
            }
        }

#if ENABLE_INTEROP
        // Works better on Windows. However, this won't work on Mono on Mac/Linux.
        [DllImport("user32", CharSet = CharSet.Auto)]
        static extern bool FlashWindow(IntPtr hWnd, bool bInvert);
        [DllImport("user32", CharSet = CharSet.Auto)]
        static extern bool GetScrollRange(IntPtr hWnd, int nBar, out int nMinPos, out int nMaxPos);
        [DllImport("user32", CharSet = CharSet.Auto)]
        static extern bool SetScrollRange(IntPtr hWnd, int nBar, int mMinPos, int mMaxPos, bool bRedraw);
        [DllImport("user32", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, POINT lParam);
        [StructLayout(LayoutKind.Sequential)]
        class POINT
        {
            public int x;
            public int y;
            public POINT(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }
        const int SB_VERT = 1;
        const int EM_SETSCROLLPOS = 0x0400 + 222;
#endif

        /// <summary>Scroll to the bottom of a textbox</summary>
        public void ScrollToBottom(RichTextBox textBox)
        {
#if ENABLE_INTEROP
            int min, max;
            GetScrollRange(textBox.Handle, SB_VERT, out min, out max);
            SendMessage(textBox.Handle, EM_SETSCROLLPOS, 0, new POINT(0, max - textBox.Height));
#else
            // Using ScrollToCaret() flickers too much.  The Interop method is better.
            // However, Interop does not work on Mono on Mac/Linux.
            if (textBox.SelectionStart != textBox.Text.Length)
            {
                this.SuspendLayout();
                textBox.SelectionLength = 0;
                textBox.SelectionStart = textBox.Text.Length;
                textBox.ScrollToCaret();
                textBox.ScrollToCaret(); // Bug requires this to be called twice in a row
                this.ResumeLayout();
            }
#endif
        }

        /// <summary>Scroll to the top of a textbox</summary>
        public void ScrollToTop(RichTextBox textBox)
        {
#if ENABLE_INTEROP
            SendMessage(textBox.Handle, EM_SETSCROLLPOS, 0, new POINT(0, 0));
#else
            if (textBox.SelectionStart != 0)
            {
                textBox.SelectionStart = 0;
                textBox.SelectionLength = 0;
                textBox.ScrollToCaret();
            }
#endif
        }

        /// <summary>Flash this chat window</summary>
        public void Flash()
        {
#if ENABLE_INTEROP
            FlashWindow(this.Handle, true);
#endif
        }

        /// <summary>Resets specified text box, including resetting its scroll bar (bug)</summary>
        private void ClearTextBox(RichTextBox textBox)
        {
            textBox.Clear();
#if ENABLE_INTEROP
            SetScrollRange(textBox.Handle, SB_VERT, 0, 0, false);
#endif
        }

        /// <summary>Menu item - close conversation</summary>
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Hide();
        }

        /// <summary>Close window event</summary>
        private void FrmChat_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClearConversation();
            this.Enabled = false;
        }

        /// <summary>Override the Form Close button</summary>
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == 0x0010)
            {
                // Windows has send the WM_CLOSE message to your form.
                // Ignore this message will make the window stay open.
                ClearConversation();
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Clear the conversation, upon close of chat</summary>
        private void ClearConversation()
        {
            if (this.IsDisposed) return;
            this.timerRTTsend.Stop();
            if (SendBox.Text != "")
            {
                // Erase the remote system's RTT message if we clear locally.
                encoderRTT.Clear();
                encoderRTT.ForceRetransmit = true;
                JabberSendRTT();
            }
            m_chat.Clear();

            // Clear text from all buffers
            ignoreChangeEvent = true;
            ClearTextBox(textboxSendMsg1);
            ClearTextBox(textboxSendMsg2);
            ClearTextBox(textboxSendMsg3);
            ClearTextBox(textboxConversation1);
            ClearTextBox(textboxConversation2);
            ClearTextBox(textboxRealTime);
            ClearTextBox(textboxParticipant1);
            ClearTextBox(textboxParticipantLocal);
            ignoreChangeEvent = false;
            
            // Reset scrollbar positions in all buffers
            ScrollToTop(textboxConversation1);
            ScrollToTop(textboxConversation2);
            ScrollToTop(textboxRealTime);
            ScrollToTop(textboxParticipant1);
            ScrollToTop(textboxParticipantLocal);
            this.Hide();
            this.ReceiveFlag = false;
        }

        /// <summary>Various modes triggered by menu</summary>
        private void menuItemEnableRTT_Click(object sender, EventArgs e)
        {
            rttEnabled = !rttEnabled;
            if (!rttEnabled)
            {
                encoderRTT.Clear();
                m_chat.ClearRealTimeMessages();
                ResetConversationMessageHistoryCache();
                UpdateConversationWindow();
            }
            UpdateMenuCheckmarks();
        }
        private void menuItemSpecificationRecommended_Click(object sender, EventArgs e)
        {
            // RECOMMENDED
            encoderRTT.TransmitInterval = 700;
            encoderRTT.KeyIntervalsEnabled = true;
            cursorChar = CURSOR_CHAR;
            UpdateConversationWindow();
            UpdateMenuCheckmarks();
        }
        private void menuItemSpecificationLowLag_Click(object sender, EventArgs e)
        {
            encoderRTT.TransmitInterval = 300;
            encoderRTT.KeyIntervalsEnabled = true;
            cursorChar = CURSOR_CHAR;
            UpdateConversationWindow();
            UpdateMenuCheckmarks();
        }
        private void menuItemSpecificationBadImmediateTransmit_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED
            encoderRTT.TransmitInterval = 0;
            encoderRTT.KeyIntervalsEnabled = false;
            cursorChar = CURSOR_CHAR;
            UpdateConversationWindow();
            UpdateMenuCheckmarks();
        }
        private void menuItemSpecificationBadBurstyText_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED
            encoderRTT.TransmitInterval = 1000;
            encoderRTT.KeyIntervalsEnabled = false;
            cursorChar = (char)0;
            UpdateConversationWindow();
            UpdateMenuCheckmarks();
        }

        /// <summary>Set custom RTT interval</summary>
        private void menuItem0ms_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED due to XMPP server flooding. Okay for private/LAN use.
            encoderRTT.TransmitInterval = 0;
            UpdateMenuCheckmarks();
        }
        private void menuItem20ms_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED due to XMPP server flooding. Okay for private/LAN use.
            encoderRTT.TransmitInterval = 20;
            UpdateMenuCheckmarks();
        }
        private void menuItem50ms_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED due to XMPP server flooding. Okay for private/LAN use.
            encoderRTT.TransmitInterval = 50;
            UpdateMenuCheckmarks();
        }
        private void menuItem100ms_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED due to XMPP server flooding. Okay for private/LAN use.
            encoderRTT.TransmitInterval = 100;
            UpdateMenuCheckmarks();
        }
        private void menuItem200ms_Click(object sender, EventArgs e)
        {
            encoderRTT.TransmitInterval = 200;
            UpdateMenuCheckmarks();
        }
        private void menuItem300ms_Click(object sender, EventArgs e)
        {
            encoderRTT.TransmitInterval = 300;
            UpdateMenuCheckmarks();
        }
        private void menuItem500ms_Click(object sender, EventArgs e)
        {
            encoderRTT.TransmitInterval = 500;
            UpdateMenuCheckmarks();
        }
        private void menuItem700ms_Click(object sender, EventArgs e)
        {
            encoderRTT.TransmitInterval = 700;
            UpdateMenuCheckmarks();
        }
        private void menuItem1000ms_Click(object sender, EventArgs e)
        {
            // RECOMMENDED (best compromise to balance usability, server, network, etc.)
            encoderRTT.TransmitInterval = 1000;
            UpdateMenuCheckmarks();
        }
        private void menuItem2000ms_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED due to extreme lag
            encoderRTT.TransmitInterval = 2000;
            UpdateMenuCheckmarks();
        }
        private void menuItem3000ms_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED due to extreme lag
            encoderRTT.TransmitInterval = 3000;
            UpdateMenuCheckmarks();
        }
        private void menuItem5000ms_Click(object sender, EventArgs e)
        {
            // NOT RECOMMENDED due to extreme lag
            encoderRTT.TransmitInterval = 5000;
            UpdateMenuCheckmarks();
        }

        /// <summary>Enable/disable embedded delays</summary>
        private void menuItemEnableKeyPressIntervals_Click(object sender, EventArgs e)
        {
            encoderRTT.KeyIntervalsEnabled = !encoderRTT.KeyIntervalsEnabled;
            UpdateMenuCheckmarks();
        }

        /// <summary>Enable/disable rendering of remote cursor</summary>
        private void menuItemEnableRemoteCursor_Click(object sender, EventArgs e)
        {
            cursorChar = (cursorChar == 0) ? CURSOR_CHAR : (char)0;
            UpdateConversationWindow();
            UpdateMenuCheckmarks();
        }

        /// <summary>Open RealJabber home page</summary>
        private void menuItemRealJabber_Click(object sender, EventArgs e)
        {
            Process.Start("http://www.realjabber.org");
        }

        /// <summary>Clears the chat</summary>
        private void menuItemClear_Click(object sender, EventArgs e)
        {
            encoderRTT.Clear();
            m_chat.Clear();
            ResetConversationMessageHistoryCache();
            UpdateConversationWindow();
        }

        private void menuItemEnableChatStates_Click(object sender, EventArgs e)
        {
            enableChatStates = !enableChatStates;
            UpdateMenuCheckmarks();
        }

        /// <summary>Update the checkmarks displayed next to menu items</summary>
        private void UpdateMenuCheckmarks()
        {
            menuItemEnableChatStates.Checked = enableChatStates;
            menuItemEnableRTT.Checked = rttEnabled;
            menuItemResetToBaseline.Checked = ((encoderRTT.TransmitInterval == 1000) && !encoderRTT.KeyIntervalsEnabled);
            menuItemNaturalTypingMode1.Checked = ((encoderRTT.TransmitInterval == 1000) && encoderRTT.KeyIntervalsEnabled);
            menuItemNaturalTypingMode2.Checked = ((encoderRTT.TransmitInterval == 500)  && encoderRTT.KeyIntervalsEnabled);
            menuItemNaturalTypingMode3.Checked = ((encoderRTT.TransmitInterval == 0)    && !encoderRTT.KeyIntervalsEnabled);
            menuItem0ms.Checked    = (encoderRTT.TransmitInterval == 0);
            menuItem20ms.Checked   = (encoderRTT.TransmitInterval == 20);
            menuItem50ms.Checked   = (encoderRTT.TransmitInterval == 50);
            menuItem100ms.Checked  = (encoderRTT.TransmitInterval == 100);
            menuItem200ms.Checked  = (encoderRTT.TransmitInterval == 200);
            menuItem300ms.Checked  = (encoderRTT.TransmitInterval == 300);
            menuItem700ms.Checked  = (encoderRTT.TransmitInterval == 500);
            menuItem1000ms.Checked = (encoderRTT.TransmitInterval == 1000);
            menuItem2000ms.Checked = (encoderRTT.TransmitInterval == 2000);
            menuItem3000ms.Checked = (encoderRTT.TransmitInterval == 3000);
            menuItem5000ms.Checked = (encoderRTT.TransmitInterval == 5000);
            menuItemEnableKeyPressIntervals.Checked = encoderRTT.KeyIntervalsEnabled;
            menuItemEnableRemoteCursor.Checked = (cursorChar != 0);
        }

        /// <summary>Launch default web browser when link is clicked</summary>
        private void textboxConversation1_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }
        private void textboxSendMsg_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }
        private void textboxConversation2_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }
        private void textboxRealTime_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }
        private void textboxParticipant1_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }
        private void textboxParticipantLocal_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }

        private void setTextSize(float size)
        {
            setFontSizeRecursive(this, size);
            m_chat.ChatUserFont = new Font(m_chat.ChatUserFont.FontFamily, size, m_chat.ChatUserFont.Style);
            m_chat.ChatTextFont = new Font(m_chat.ChatTextFont.FontFamily, size, m_chat.ChatTextFont.Style);
            cachedRTF1.Clear();
            cachedRTF2.Clear();
            UpdateConversationWindow();
            ScrollToBottom(textboxConversation1);
        }

        private void setFontSizeRecursive(Control control, float size)
        {
            control.Font = new Font(control.Font.FontFamily, size, control.Font.Style); 
            foreach (Control ctl in control.Controls)
            {
                setFontSizeRecursive(ctl, size);
            }
        }

        private void menuItemTextSmall_Click(object sender, EventArgs e)
        {
            setTextSize(TEXTSIZE_SMALL); 
        }

        private void menuItemTextMedium_Click(object sender, EventArgs e)
        {
            setTextSize(TEXTSIZE_MEDIUM); 
        }

        private void menuItemTextLarge_Click(object sender, EventArgs e)
        {
            setTextSize(TEXTSIZE_LARGE);
        }

        private void menuItemTextHuge_Click(object sender, EventArgs e)
        {
            setTextSize(TEXTSIZE_HUGE);
        }

        private void FrmChat_SizeChanged(object sender, EventArgs e)
        {
            ScrollToBottom(textboxConversation1);
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShairportSharp.Http
{
    class HttpMessageBuffer : MessageBuffer
    {
        static readonly byte[] headerDelimiter = { 0x0D, 0x0A, 0x0D, 0x0A }; // \r\n\r\n

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        HttpMessage partialMessage;

        public HttpMessageBuffer()
            : base(headerDelimiter)
        { }

        protected override int OnMessage(byte[] messageData)
        {
            int contentLength;
            partialMessage = HttpMessage.FromBytes(messageData, 0, messageData.Length, out contentLength);
            if (contentLength == 0 && MessageReceived != null)
            {
                MessageReceived(this, new MessageReceivedEventArgs(partialMessage));
                partialMessage = null;
            }
            return contentLength;
        }

        protected override void OnContent(byte[] content)
        {
            if (partialMessage != null)
            {
                partialMessage.Content = content;
                if (MessageReceived != null)
                    MessageReceived(this, new MessageReceivedEventArgs(partialMessage));
                partialMessage = null;
            }
        }
    }

    class MessageReceivedEventArgs : EventArgs
    {
        public MessageReceivedEventArgs(HttpMessage message)
        {
            this.message = message;
        }

        HttpMessage message;
        public HttpMessage Message { get { return message; } }
    }
}

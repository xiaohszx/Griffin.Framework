﻿using System;
using System.CodeDom.Compiler;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using FluentAssertions;
using Griffin.Net;
using Griffin.Net.Buffers;
using Griffin.Net.Channels;
using Griffin.Net.Protocols;
using Griffin.Net.Protocols.Strings;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Griffin.Core.Tests.Net.Channels
{
    public class SecureTcpChannelTests : IDisposable
    {
        private ClientServerHelper _helper;
        private X509Certificate2 _certificate;

        public SecureTcpChannelTests()
        {
            _helper = ClientServerHelper.Create();
            _certificate = new X509Certificate2(
                AppDomain.CurrentDomain.BaseDirectory + "\\Net\\cert\\GriffinNetworkingTemp.pfx", "mamma");
        }

        private SecureTcpChannel CreateClientChannel(IBufferSlice slice, IMessageEncoder encoder, IMessageDecoder decoder)
        {
            var streamBuilder = new ClientSideSslStreamBuilder("mamma");
            var channel = new SecureTcpChannel(slice, encoder, decoder, streamBuilder);
            return channel;
        }

        private SecureTcpChannel CreateServerChannel(IBufferSlice slice, IMessageEncoder encoder, IMessageDecoder decoder)
        {

            var streamBuilder = new ServerSideSslStreamBuilder(_certificate);
            var channel = new SecureTcpChannel(slice, encoder, decoder, streamBuilder);
            return channel;
        }

        [Fact]
        public void Assign_should_work_after_subscription()
        {
            var slice = new BufferSlice(new byte[65535], 0, 65535);
            var encoder = Substitute.For<IMessageEncoder>();
            var decoder = Substitute.For<IMessageDecoder>();
            object expected;
            var stream = new SslStream(new NetworkStream(_helper.Server));
            stream.BeginAuthenticateAsServer(_certificate, OnAuthenticated, stream);

            var sut = CreateClientChannel(slice, encoder, decoder);
            sut.MessageReceived += (channel, message) => expected = message;
            sut.Assign(_helper.Client);

        }

        [Fact]
        public void assign_without_subscribing_on_MessageReceived_means_that_messages_can_get_lost()
        {
            var slice = new BufferSlice(new byte[65535], 0, 65535);
            var encoder = Substitute.For<IMessageEncoder>();
            var decoder = Substitute.For<IMessageDecoder>();
            var stream = new SslStream(new NetworkStream(_helper.Server));
            stream.BeginAuthenticateAsServer(_certificate, OnAuthenticated, stream);

            var sut = CreateClientChannel(slice, encoder, decoder);
            Action actual = () => sut.Assign(_helper.Client);

            actual.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void should_listen_on_the_decoder_event()
        {
            var slice = new BufferSlice(new byte[65535], 0, 65535);
            var encoder = Substitute.For<IMessageEncoder>();
            var decoder = new FakeDecoder();
            object expected = null;
            var stream = new SslStream(new NetworkStream(_helper.Server));
            stream.BeginAuthenticateAsServer(_certificate, OnAuthenticated, stream);

            var sut = CreateClientChannel(slice, encoder, decoder);
            sut.MessageReceived += (channel, message) => expected = message;
            decoder.MessageReceived("Hello");

            expected.Should().Be("Hello");
        }


        [Fact]
        public void send_message()
        {
            var slice = new BufferSlice(new byte[65535], 0, 65535);
            var encoder = new StringEncoder();
            var decoder = new StringDecoder();

            var sut = CreateClientChannel(slice, encoder, decoder);
            sut.MessageReceived += (channel, message) =>
            {
            };
            var stream = new SslStream(new NetworkStream(_helper.Server));
            stream.BeginAuthenticateAsServer(_certificate, OnAuthenticated, stream);
            sut.Assign(_helper.Client);
            sut.Send("Hello world");

            //i do not know why this loop is required.
            //for some reason the send message is divided into two tcp packets 
            //when using SslStream.
            var bytesReceived = 0;
            var buf = new byte[65535];
            while (bytesReceived < 15)
                bytesReceived += stream.Read(buf, bytesReceived, 15);

            var actual = Encoding.ASCII.GetString(buf, 4, bytesReceived - 4); // string encoder have a length header.
            actual.Should().Be("Hello world");
        }

        [Fact]
        public void send_close_message()
        {
            var slice = new BufferSlice(new byte[65535], 0, 65535);
            var encoder = new StringEncoder();
            var decoder = new StringDecoder();

            var sut = CreateClientChannel(slice, encoder, decoder);
            sut.MessageReceived += (channel, message) => { };
            var stream = new SslStream(new NetworkStream(_helper.Server));
            stream.BeginAuthenticateAsServer(_certificate, OnAuthenticated, stream);
            sut.Assign(_helper.Client);

            Assert.True(sut.IsConnected);

            sut.Close();

            Assert.False(sut.IsConnected);
        }

        private void OnAuthenticated(IAsyncResult ar)
        {
            try
            {
                var stream = (SslStream)ar.AsyncState;
                stream.EndAuthenticateAsServer(ar);
            }
            catch { }
        }

        [Fact]
        public void Receive_one_message()
        {
            var slice1 = new BufferSlice(new byte[65535], 0, 65535);
            var encoder1 = new StringEncoder();
            var decoder1 = new StringDecoder();
            var expected = "Hello".PadRight(5000);
            var outBuffer = new byte[expected.Length + 4];
            object actual = null;
            var evt = new ManualResetEvent(false);
            var stream = new SslStream(new NetworkStream(_helper.Server));
            BitConverter2.GetBytes(expected.Length, outBuffer, 0);
            Encoding.UTF8.GetBytes(expected, 0, expected.Length, outBuffer, 4);
            stream.BeginAuthenticateAsServer(_certificate, OnAuthenticated, stream);

            var sut1 = CreateClientChannel(slice1, encoder1, decoder1);
            sut1.MessageReceived = (channel, message) =>
            {
                actual = message;
                evt.Set();
            };
            sut1.Assign(_helper.Client);
            evt.WaitOne(500); // wait on authentication
            evt.Reset();
            stream.Write(outBuffer);

            evt.WaitOne(500).Should().BeTrue();
            actual.Should().Be(expected);
        }





        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _helper.Dispose();
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Griffin.Net.Buffers;

namespace Griffin.Net.Channels
{
    /// <summary>
    ///     A communication channel implemented using TCP.
    /// </summary>
    public class TcpChannel : IBinaryChannel
    {
        private readonly List<IPooledObject> _pooledWriteBuffers = new List<IPooledObject>();
        private readonly List<SendPacketsElement> _queuedOutboundPackets = new List<SendPacketsElement>();
        private readonly object _writeBufferQueueLock = new object();
        private AsyncSocket _socket;
        private volatile ChannelState _state;

        public TcpChannel()
        {
            MaxBytesPerWriteOperation = 65535;
            ChannelData = new ChannelData();
            RemoteEndpoint = EmptyEndpoint.Instance;
        }

        /// <summary>
        /// </summary>
        internal ChannelState State => _state;

        /// <summary>
        ///     Address to the end point that we should connect to (or address to the EP if this is a server side channel).
        /// </summary>
        /// <remarks>
        ///     <para>Typically a <see cref="IPEndPoint" /> or <see cref="DnsEndPoint" />.</para>
        /// </remarks>
        public EndPoint RemoteEndpoint { get; private set; }

        /// <summary>
        ///     Indicates if the channel is open or not.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The reliability of this flag varies depending on the communication technology. The connection
        ///         might be down due to network failures etc. hence it tells what the channel sees as the current truth,
        ///         but that can change with the next IO operation which would challenge that truth.
        ///     </para>
        /// </remarks>
        public bool IsOpen => State == ChannelState.Open;

        /// <summary>
        ///     You can use channel data to store connection specific information (compare it to a HTTP session).
        /// </summary>
        public IChannelData ChannelData { get; }

        /// <summary>
        ///     Channel data is an dictionary which means a lookup every time. This token can be used as an alternative.
        /// </summary>
        public object UserToken { get; set; }

        public bool IsConnected => _socket?.IsConnected == true;

        /// <summary>
        ///     Amount of bytes which can be included in a send operation.
        /// </summary>
        public int MaxBytesPerWriteOperation { get; set; }

        /// <summary>
        ///     Channel have been closed (by either side).
        /// </summary>
        public event EventHandler ChannelClosed;

        public async Task CloseAsync()
        {
            await _socket.CloseAsync();
            ChannelClosed?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc />
        public void Assign(Socket socket)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (_socket != null)
                throw new InvalidOperationException("Close() first.");
            _socket = new AsyncSocket();
            _socket.Assign(socket);
        }

        public async Task OpenAsync()
        {
            if (RemoteEndpoint == EmptyEndpoint.Instance)
                throw new InvalidOperationException("No remote endpoint have been specified.");

            _socket = new AsyncSocket();
            await _socket.ConnectAsync(RemoteEndpoint);
        }

        public async Task OpenAsync(EndPoint endpoint)
        {
            RemoteEndpoint = endpoint;
            await OpenAsync();
        }

        void IBinaryChannel.Reset()
        {
            Reset();
        }

        public async Task SendAsync(byte[] buffer, int offset, int count)
        {
            if (!_queuedOutboundPackets.Any())
            {
                await _socket.SendAsync(buffer, offset, count);
                return;
            }

            _queuedOutboundPackets.Add(new SendPacketsElement(buffer, offset, count));
            await SendAllQueuedBuffers();
        }

        public async Task SendAsync(IEnumerable<IBufferSegment> buffers)
        {
            await SendAllQueuedBuffers();
            var packets = buffers
                .Select(x => new SendPacketsElement(x.Buffer, x.Offset, x.Count))
                .ToArray();
            await _socket.SendAsync(packets);
        }

        public async Task SendAsync(IBufferSegment buffer)
        {
            await SendAllQueuedBuffers();
            await _socket.SendAsync(buffer.Buffer, buffer.Offset, buffer.Count);
        }

        public async Task SendMoreAsync(byte[] buffer, int offset, int count)
        {
            _queuedOutboundPackets.Add(new SendPacketsElement(buffer, offset, count));
            if (_queuedOutboundPackets.Sum(x => x.Count) < MaxBytesPerWriteOperation)
                return;

            await SendAllQueuedBuffers();
        }

        public async Task SendMoreAsync(IBufferSegment segment)
        {
            var s = new SendPacketsElement(segment.Buffer, segment.Offset, segment.Count);
            _queuedOutboundPackets.Add(s);

            if (segment is IPooledObject p)
                _pooledWriteBuffers.Add(p);

            if (_queuedOutboundPackets.Sum(x => x.Count) < MaxBytesPerWriteOperation)
                return;

            await SendAllQueuedBuffers();
        }

        //public async Task SendMoreAsync(byte[] buffer, int offset, int count, bool deliverIfChannelIsFree)
        //{
        //    _queuedOutboundPackets.Add(new SendPacketsElement(buffer, offset, count));
        //    if (_queuedOutboundPackets.Sum(x => x.Count) < MaxBytesPerWriteOperation || _socket.IsWritePending)
        //        return;

        //    await SendAllQueuedBuffers();
        //}


        private void Reset()
        {
            RemoteEndpoint = EmptyEndpoint.Instance;
            _state = ChannelState.Closed;
            _socket = null;
            _queuedOutboundPackets.Clear();
            foreach (var writeBuffer in _pooledWriteBuffers) writeBuffer.ReturnToPool();

            _pooledWriteBuffers.Clear();
        }

        private async Task SendAllQueuedBuffers()
        {
            if (_queuedOutboundPackets.Count == 0)
                return;

            var packets = _queuedOutboundPackets.ToArray();
            await _socket.SendAsync(packets);
            _queuedOutboundPackets.Clear();

            foreach (var writeBuffer in _pooledWriteBuffers) writeBuffer.ReturnToPool();
            _pooledWriteBuffers.Clear();
        }

        /// <summary>
        ///     Receive bytes from the remote endpoint.
        /// </summary>
        /// <param name="readBuffer">Buffer to fill with data.</param>
        /// <returns></returns>
        /// <remarks>
        ///     <para>
        ///         This method will receive from the current offset and up to the amount of bytes that are left from the current
        ///         offset to the end of the buffer.
        ///         Once the read completes, it will increase the <c>Count</c> with the number of received bytes.
        ///     </para>
        /// </remarks>
        public async Task<int> ReceiveAsync(IBufferSegment readBuffer)
        {
            var usedCount = readBuffer.Offset - readBuffer.StartOffset;
            var bytesReceived = await _socket.ReceiveAsync(readBuffer.Buffer, readBuffer.Offset, readBuffer.Capacity - usedCount);
            readBuffer.Count += bytesReceived;
            return bytesReceived;
        }
    }
}
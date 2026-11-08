using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Npgsql;
namespace CP6.Tests.Erp;
// Owned loopback wire fault: PostgreSQL finishes COMMIT, then its reply is dropped. No SQL result is fabricated.
internal sealed class PrivacyRestoreCommitReplyProxyFixture:IAsyncDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource stop=new();
    private readonly List<Task> connections=[];
    private readonly Task accepting;
    private readonly NpgsqlConnectionStringBuilder backend;
    private int armed;
    internal string Connection{get;}
    internal TaskCompletionSource Dropped{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Arm()=>Interlocked.Exchange(ref armed,1);
    internal PrivacyRestoreCommitReplyProxyFixture(string connection)
    {
        backend=new(connection);
        if(!IPAddress.TryParse(backend.Host,out var address)||!IPAddress.IsLoopback(address))throw new InvalidOperationException("PRIVACY_COMMIT_PROXY_REQUIRES_OWNED_LOOPBACK");
        listener.Start();Connection=new NpgsqlConnectionStringBuilder(connection){Host="127.0.0.1",Port=((IPEndPoint)listener.LocalEndpoint).Port,Pooling=false,SslMode=SslMode.Disable}.ConnectionString;
        accepting=Accept();
    }
    private async Task Accept()
    {
        try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);connections.Add(Forward(client));}}
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        catch(SocketException)when(stop.IsCancellationRequested){}
    }
    private async Task Forward(TcpClient client)
    {
        using(client)using(var upstream=new TcpClient())
        {
            await upstream.ConnectAsync(backend.Host,backend.Port,stop.Token);
            var source=client.GetStream();var target=upstream.GetStream();var dropCommit=0;
            async Task<byte[]?> Read(Stream stream,int length)
            {
                var value=new byte[length];var offset=0;
                while(offset<length){var n=await stream.ReadAsync(value.AsMemory(offset),stop.Token);if(n==0)return null;offset+=n;}
                return value;
            }
            async Task Client()
            {
                var header=await Read(source,4);if(header==null)return;var length=BinaryPrimitives.ReadInt32BigEndian(header);
                var startup=await Read(source,length-4);if(startup==null)return;
                if(BinaryPrimitives.ReadInt32BigEndian(startup)!=196608)throw new InvalidOperationException("PRIVACY_COMMIT_PROXY_EXPECTED_PLAIN_OWNED_STARTUP");
                await target.WriteAsync(header,stop.Token);await target.WriteAsync(startup,stop.Token);
                while(true)
                {
                    var frame=await Read(source,5);if(frame==null)return;var body=await Read(source,BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(1))-4);if(body==null)return;
                    var query=frame[0]=='Q'?Encoding.UTF8.GetString(body).TrimEnd('\0'):"";
                    if(frame[0]=='P'){var end=Array.IndexOf(body,(byte)0);var sqlEnd=Array.IndexOf(body,(byte)0,end+1);query=Encoding.UTF8.GetString(body,end+1,sqlEnd-end-1);}
                    if(query.Trim().TrimEnd(';').Equals("COMMIT",StringComparison.OrdinalIgnoreCase)&&Interlocked.Exchange(ref armed,0)==1)Interlocked.Exchange(ref dropCommit,1);
                    await target.WriteAsync(frame,stop.Token);await target.WriteAsync(body,stop.Token);
                }
            }
            async Task Server()
            {
                while(true)
                {
                    var frame=await Read(target,5);if(frame==null)return;var body=await Read(target,BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(1))-4);if(body==null)return;
                    if(frame[0]=='C'&&Encoding.UTF8.GetString(body).TrimEnd('\0')=="COMMIT"&&Volatile.Read(ref dropCommit)==1)
                    {Dropped.TrySetResult();return;}
                    await source.WriteAsync(frame,stop.Token);await source.WriteAsync(body,stop.Token);
                }
            }
            var request=Client();var response=Server();
            try{await Task.WhenAny(request,response);}
            finally{client.Close();upstream.Close();}
            try{await Task.WhenAll(request,response);}
            catch(Exception e)when(e is IOException or SocketException or OperationCanceledException or ObjectDisposedException){}
        }
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();listener.Stop();await accepting;await Task.WhenAll(connections);stop.Dispose();
    }
}

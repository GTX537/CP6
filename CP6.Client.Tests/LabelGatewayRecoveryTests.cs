using System.Net;
using System.Net.Http.Json;
using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Client.Tests;

public sealed class LabelGatewayRecoveryTests
{
    [Fact]
    public async Task Poll_uses_the_actual_PostgreSql_pending_status()
    {
        var printer=new Printer();var state=new Server(strictStatus:true);
        await Service(state,printer).ProcessOnceAsync();
        Assert.Equal(1,printer.Calls);Assert.Equal("PENDING",state.QueriedStatus);
    }

    [Fact]
    public async Task Completion_response_loss_never_sends_a_different_fail_command()
    {
        var printer=new Printer();var state=new Server{LoseCompletion=true};
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());
        Assert.Equal(1,printer.Calls);Assert.Equal(1,state.CompleteCalls);Assert.Equal(0,state.FailCalls);
    }

    [Fact]
    public async Task Printer_exception_after_possible_output_does_not_prove_no_print()
    {
        var printer=new Printer{AfterOutput=()=>throw new IOException("simulated uncertain printer output")};var state=new Server();
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());
        Assert.Equal(1,printer.Calls);Assert.Equal(0,state.CompleteCalls);Assert.Equal(0,state.FailCalls);
    }

    [Fact]
    public async Task Cold_completion_reuses_the_original_command_without_printing_again()
    {
        var state=new Server{LoseCompletion=true};var printer=new Printer();
        Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));
        var store=new FileLabelGatewayJournal(state.Directory);var pending=Assert.Single(await store.ReadOpenAsync(state.Identity));
        Assert.Equal(LabelGatewayStage.CompletionPending,pending.Stage);Assert.Equal(1,printer.Calls);
        state.LoseCompletion=false;await Service(state,printer).ProcessOnceAsync();
        var complete=await store.ReadAsync(state.Identity,"LBL-SIMULATED");Assert.Equal(LabelGatewayStage.Completed,complete!.Stage);
        Assert.Equal(1,printer.Calls);Assert.Equal(0,state.FailCalls);Assert.Equal(2,state.CompletionOperations.Count);
        Assert.All(state.CompletionOperations,id=>Assert.Equal(pending.CompletionOperationId,id));
        foreach(var file in System.IO.Directory.GetFiles(state.Directory,"*.json",SearchOption.AllDirectories))
        {var text=await File.ReadAllTextAsync(file);Assert.DoesNotContain("PRIVATE-TEMPLATE",text);Assert.DoesNotContain("business",text);}
    }

    [Fact]
    public async Task Cold_claim_response_loss_recovers_its_original_key_before_first_print()
    {
        var state=new Server{LoseClaimOnce=true};var printer=new Printer();
        Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));Assert.Equal(0,printer.Calls);
        await Service(state,printer).ProcessOnceAsync();Assert.Equal(1,printer.Calls);Assert.Equal(2,state.ClaimOperations.Count);
        Assert.Equal(state.ClaimOperations[0],state.ClaimOperations[1]);Assert.Equal(0,state.FailCalls);
    }

    [Fact]
    public async Task Cold_uncertain_print_stays_visible_and_never_automatically_repeats()
    {
        var state=new Server();var printer=new Printer{AfterOutput=()=>throw new IOException("possible output before disconnection")};
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());
        var cold=Service(state,printer);var statuses=new List<string>();cold.StateChanged+=(_,s)=>statuses.Add(s);
        var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>cold.ProcessOnceAsync()));
        Assert.Equal("PrintOutcomeUnknown",error.Code);Assert.Contains("PrintOutcomeUnknown",statuses);
        Assert.Equal(1,printer.Calls);Assert.Equal(0,state.CompleteCalls);Assert.Equal(0,state.FailCalls);
    }

    [Fact]
    public async Task Cancellation_after_possible_print_output_survives_a_new_instance()
    {
        var state=new Server();using var cancel=new CancellationTokenSource();
        var printer=new Printer{AfterOutput=()=>{cancel.Cancel();throw new OperationCanceledException(cancel.Token);}};
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync(cancel.Token));
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());
        Assert.Equal(1,printer.Calls);Assert.Equal(0,state.CompleteCalls);Assert.Equal(0,state.FailCalls);
    }

    [Fact]
    public async Task Concurrent_instances_have_one_effective_worker_for_the_durable_scope()
    {
        var state=new Server();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var printer=new Printer{AfterOutputAsync=async ct=>{entered.SetResult();await release.Task.WaitAsync(ct);}};
        var first=Service(state,printer).ProcessOnceAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));Assert.Equal("GatewayBusy",error.Code);}
        finally{release.SetResult();}
        await first;Assert.Equal(1,printer.Calls);Assert.Single(state.ClaimOperations);Assert.Single(state.CompletionOperations);
    }

    [Fact]
    public async Task Unwritable_journal_fails_before_any_network_or_print()
    {
        var state=new Server();var printer=new Printer();System.IO.Directory.CreateDirectory(Path.GetDirectoryName(state.Directory)!);await File.WriteAllTextAsync(state.Directory,"file-blocks-directory");
        Assert.IsAssignableFrom<IOException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));Assert.Equal(0,state.Requests);Assert.Equal(0,printer.Calls);
    }

    [Fact]
    public async Task Corrupt_journal_fails_before_network_and_retains_unknown_print_history()
    {
        var state=new Server();var printer=new Printer{AfterOutput=()=>throw new IOException("possible output")};
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());var calls=state.Requests;
        var file=Assert.Single(System.IO.Directory.GetFiles(state.Directory,"*.json",SearchOption.AllDirectories));await File.WriteAllTextAsync(file,"{");
        var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));
        Assert.Equal("RecoveryJournalInvalid",error.Code);Assert.Equal(calls,state.Requests);Assert.Equal(1,printer.Calls);
    }

    [Fact]
    public async Task Renamed_recovery_record_is_rejected_before_network_or_print()
    {
        var state=new Server{LoseCompletion=true};var printer=new Printer();
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());var requests=state.Requests;
        var file=Assert.Single(System.IO.Directory.GetFiles(state.Directory,"*.json",SearchOption.AllDirectories));
        File.Move(file,Path.Combine(Path.GetDirectoryName(file)!,"wrong-job.json"));
        var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));
        Assert.Equal("RecoveryJournalInvalid",error.Code);Assert.Equal(requests,state.Requests);Assert.Equal(1,printer.Calls);
    }

    [Fact]
    public async Task Existing_uncertain_output_prevents_further_polling_and_stays_visible()
    {
        var state=new Server();var printer=new Printer{AfterOutput=()=>throw new IOException("possible output")};
        _=await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());var requests=state.Requests;
        var cold=Service(state,printer);var statuses=new List<string>();cold.StateChanged+=(_,value)=>statuses.Add(value);
        var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>cold.ProcessOnceAsync()));
        Assert.Equal("PrintOutcomeUnknown",error.Code);Assert.Equal(requests,state.Requests);Assert.Equal(1,printer.Calls);
        Assert.Equal("PrintOutcomeUnknown",Assert.Single(statuses));
    }

    [Theory]
    [InlineData("source")][InlineData("tenant")][InlineData("actor")][InlineData("device")]
    public async Task Recovery_records_are_partitioned_by_the_actual_context(string part)
    {
        var state=new Server{LoseCompletion=true};var printer=new Printer();_ = await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync());
        var original=state.Identity;var count=state.CompleteCalls;
        state.Identity=part switch{"source"=>original with{Source=new Uri("https://different-cp6.test/")},"tenant"=>original with{TenantId=Guid.NewGuid()},"actor"=>original with{ActorId=Guid.NewGuid()},_=>original with{DeviceId="OTHER-GW"}};
        state.LoseCompletion=false;await Service(state,printer).ProcessOnceAsync();Assert.Equal(count,state.CompleteCalls);Assert.Equal(1,printer.Calls);
        Assert.Equal(LabelGatewayStage.CompletionPending,(await new FileLabelGatewayJournal(state.Directory).ReadAsync(original,"LBL-SIMULATED"))!.Stage);
    }

    [Fact]
    public async Task A_context_change_after_claim_response_cannot_reach_the_printer()
    {
        var state=new Server();var printer=new Printer();state.AfterClaim=()=>state.Identity=state.Identity with{ActorId=Guid.NewGuid()};
        var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));
        Assert.Equal("RecoveryScopeChanged",error.Code);Assert.Equal(0,printer.Calls);Assert.Equal(0,state.CompleteCalls);Assert.Equal(0,state.FailCalls);
    }

    [Fact]
    public async Task A_context_change_during_durable_print_start_cannot_reach_the_printer()
    {
        var state=new Server();var printer=new Printer();
        var journal=new OnSaveJournal(new FileLabelGatewayJournal(state.Directory),()=>state.Identity=state.Identity with{ActorId=Guid.NewGuid()});
        var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer,journal).ProcessOnceAsync()));
        Assert.Equal("RecoveryScopeChanged",error.Code);Assert.Equal(0,printer.Calls);Assert.Equal(0,state.CompleteCalls);Assert.Equal(0,state.FailCalls);
    }

    [Fact]
    public async Task Failure_persisting_print_start_prevents_the_external_call()
    {
        var state=new Server();var printer=new Printer();var journal=new FaultJournal(new FileLabelGatewayJournal(state.Directory),LabelGatewayStage.PrintStarted);
        Assert.IsAssignableFrom<IOException>(await Record.ExceptionAsync(()=>Service(state,printer,journal).ProcessOnceAsync()));Assert.Equal(0,printer.Calls);
        Assert.Equal(LabelGatewayStage.ReadyToPrint,(await new FileLabelGatewayJournal(state.Directory).ReadAsync(state.Identity,"LBL-SIMULATED"))!.Stage);
    }

    [Fact]
    public async Task Crash_window_after_printer_return_keeps_print_started_and_never_reprints()
    {
        var state=new Server();var printer=new Printer();var journal=new FaultJournal(new FileLabelGatewayJournal(state.Directory),LabelGatewayStage.CompletionPending);
        Assert.IsAssignableFrom<IOException>(await Record.ExceptionAsync(()=>Service(state,printer,journal).ProcessOnceAsync()));Assert.Equal(1,printer.Calls);
        var error=Assert.IsType<LabelGatewayRecoveryException>(await Record.ExceptionAsync(()=>Service(state,printer).ProcessOnceAsync()));
        Assert.Equal("PrintOutcomeUnknown",error.Code);Assert.Equal(1,printer.Calls);Assert.Equal(0,state.CompleteCalls);Assert.Equal(0,state.FailCalls);
    }

    private static LabelGatewayService Service(Server server,Printer printer,ILabelGatewayJournal? journal=null)
    {
        var client=new HttpClient(server,disposeHandler:false){BaseAddress=server.Identity.Source};
        var options=new ClientOptions{ApiBaseAddress=client.BaseAddress,Platform="Windows",LabelJournalDirectory=server.Directory,Context=new(){ClientKind="desktop",DeviceId=server.Identity.DeviceId}};
        return new LabelGatewayService(new Clients(client),options,printer,journal??new FileLabelGatewayJournal(server.Directory),new Context(server));
    }

    private sealed class Context(Server server):ILabelGatewayContext {public LabelGatewayIdentity Capture()=>server.Identity;}
    private sealed class FaultJournal(ILabelGatewayJournal inner,LabelGatewayStage fail):ILabelGatewayJournal
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(LabelGatewayIdentity id,CancellationToken ct=default)=>inner.TryAcquireAsync(id,ct);
        public Task<IReadOnlyList<LabelGatewayJournalEntry>> ReadOpenAsync(LabelGatewayIdentity id,CancellationToken ct=default)=>inner.ReadOpenAsync(id,ct);
        public Task<LabelGatewayJournalEntry?> ReadAsync(LabelGatewayIdentity id,string job,CancellationToken ct=default)=>inner.ReadAsync(id,job,ct);
        public Task SaveAsync(LabelGatewayIdentity id,LabelGatewayJournalEntry entry,CancellationToken ct=default)=>entry.Stage==fail?throw new IOException("simulated durable write failure"):inner.SaveAsync(id,entry,ct);
    }
    private sealed class OnSaveJournal(ILabelGatewayJournal inner,Action afterStart):ILabelGatewayJournal
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(LabelGatewayIdentity id,CancellationToken ct=default)=>inner.TryAcquireAsync(id,ct);
        public Task<IReadOnlyList<LabelGatewayJournalEntry>> ReadOpenAsync(LabelGatewayIdentity id,CancellationToken ct=default)=>inner.ReadOpenAsync(id,ct);
        public Task<LabelGatewayJournalEntry?> ReadAsync(LabelGatewayIdentity id,string job,CancellationToken ct=default)=>inner.ReadAsync(id,job,ct);
        public async Task SaveAsync(LabelGatewayIdentity id,LabelGatewayJournalEntry entry,CancellationToken ct=default)
        {await inner.SaveAsync(id,entry,ct);if(entry.Stage==LabelGatewayStage.PrintStarted)afterStart();}
    }

    private sealed class Clients(HttpClient client):IHttpClientFactory
    {public HttpClient CreateClient(string name)=>client;}
    private sealed class Printer:ILabelPrinter
    {
        public int Calls;public Action? AfterOutput;public Func<CancellationToken,Task>? AfterOutputAsync;
        public async Task PrintAsync(LabelJob job,CancellationToken ct=default){Interlocked.Increment(ref Calls);AfterOutput?.Invoke();if(AfterOutputAsync!=null)await AfterOutputAsync(ct);}
    }
    private sealed class Server(bool strictStatus=false):HttpMessageHandler
    {
        public bool LoseCompletion,LoseClaimOnce;public int CompleteCalls,FailCalls,Requests;public string? QueriedStatus;
        public Action? AfterClaim;
        public LabelGatewayIdentity Identity=new(new Uri("https://cp6-label.test/"),Guid.NewGuid(),Guid.NewGuid(),"GW-TEST");
        public string Directory=Path.Combine(Environment.GetEnvironmentVariable("CP6_LABEL_CLIENT_ROOT")??Path.Combine(Path.GetTempPath(),"CP6.LabelGatewayTests"),Guid.NewGuid().ToString("N"));
        private string _status="PENDING";private Guid? _claim,_complete;
        public List<Guid> ClaimOperations=new();
        public List<Guid> CompletionOperations=new();
        public readonly Guid Creation=Guid.NewGuid();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Get)
            {
                QueriedStatus=request.RequestUri.Query.Split('&').Select(p=>p.TrimStart('?').Split('=')).Where(p=>p.Length==2&&p[0]=="status").Select(p=>Uri.UnescapeDataString(p[1])).SingleOrDefault();
                var visible=(!strictStatus||QueriedStatus=="PENDING")&&_status=="PENDING";
                return Json(new PagedResult<LabelJob>{Items=visible?new[]{Job("PENDING")}:Array.Empty<LabelJob>(),Total=visible?1:0,Page=1,PageSize=200});
            }
            if(path.EndsWith("/claim"))
            {
                var command=await request.Content!.ReadFromJsonAsync<LabelJobCommand>(ct);ClaimOperations.Add(command!.OperationId);
                if(_claim!=null&&_claim!=command.OperationId)return new(HttpStatusCode.Conflict);
                _claim=command.OperationId;_status="PRINTING";AfterClaim?.Invoke();
                if(LoseClaimOnce){LoseClaimOnce=false;throw new HttpRequestException("simulated committed claim response loss");}
                return Json(Job("PRINTING"));
            }
            if(path.EndsWith("/complete"))
            {
                CompleteCalls++;var command=await request.Content!.ReadFromJsonAsync<LabelJobCommand>(ct);CompletionOperations.Add(command!.OperationId);
                if(_complete!=null&&_complete!=command.OperationId)return new(HttpStatusCode.Conflict);
                _complete=command.OperationId;_status="COMPLETED";
                if(LoseCompletion)throw new HttpRequestException("simulated completion response loss after server commit");return Json(Job("COMPLETED"));
            }
            if(path.EndsWith("/fail")){FailCalls++;return Json(Job("FAILED"));}
            throw new InvalidOperationException("Unexpected label request: "+path);
        }
        private LabelJob Job(string status)=>new(){JobNo="LBL-SIMULATED",OperationId=Creation,WarehouseCd="W1",RequestedDeviceId="GW-TEST",Status=status,
            Format="ZPL",TemplateBody="PRIVATE-TEMPLATE",PayloadJson="{\"private\":\"business\"}",RowVersion=Convert.ToBase64String(new byte[]{1,2,3}),AttemptCount=status=="PENDING"?0:1};
        private static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    }
}

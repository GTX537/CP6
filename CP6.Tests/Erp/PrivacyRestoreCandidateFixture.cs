using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
namespace CP6.Tests.Erp;
// Native archive/restore into a separately identified, uniquely owned test database. Never overwrites the source.
internal sealed class PrivacyRestoreCandidateFixture : IAsyncDisposable
{
    internal Guid Id{get;}=Guid.NewGuid();
    internal string Connection{get;private set;}="";
    internal string Sha256{get;private set;}="";
    internal string Name{get;private set;}="";
    internal string Directory{get;private set;}="";
    private long oid;private string admin="";private bool created;
    internal static async Task<PrivacyRestoreCandidateFixture> Create(string source)
    {
        var x=new PrivacyRestoreCandidateFixture();var s=new NpgsqlConnectionStringBuilder(source);x.Name="cp6_privacy_restore_"+x.Id.ToString("N");
        x.Directory=Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,x.Name);System.IO.Directory.CreateDirectory(x.Directory);
        x.admin=new NpgsqlConnectionStringBuilder(source){Database="postgres",Pooling=false}.ConnectionString;
        x.Connection=new NpgsqlConnectionStringBuilder(source){Database=x.Name,Pooling=false}.ConnectionString;
        await using var db=new NpgsqlConnection(x.admin);await db.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE DATABASE \"{x.Name}\"",db))await create.ExecuteNonQueryAsync();x.created=true;
        await using(var mark=new NpgsqlCommand($"COMMENT ON DATABASE \"{x.Name}\" IS 'CP6-PRIVACY-RESTORE-CANDIDATE/v1/{x.Id:D}'",db))await mark.ExecuteNonQueryAsync();
        await using(var identity=new NpgsqlCommand("SELECT oid::bigint FROM pg_database WHERE datname=@name",db)){identity.Parameters.AddWithValue("name",x.Name);x.oid=(long)(await identity.ExecuteScalarAsync())!;}
        try
        {
            var archive=Path.Combine(x.Directory,"before-disposition.dump");
            var dumpVersion=(await x.Native(s,"pg_dump",["--version"])).Trim();var restoreVersion=(await x.Native(s,"pg_restore",["--version"])).Trim();
            await using var serverVersionCommand=new NpgsqlCommand("SHOW server_version",db);var serverVersion=(string)(await serverVersionCommand.ExecuteScalarAsync())!;
            await x.Native(s,"pg_dump",["-Fc","--file",archive,"--dbname",s.Database!]);
            x.Sha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))).ToLowerInvariant();
            await x.Native(s,"pg_restore",["--exit-on-error","--single-transaction","--dbname",x.Name,archive]);
            // This fixture has no per-database role settings/ACL overrides. Verify instead of silently losing them.
            const string metadata="SELECT jsonb_build_object('owner',datdba::regrole::text,'acl',datacl::text,'settings',COALESCE((SELECT jsonb_agg(jsonb_build_object('role',setrole,'config',setconfig) ORDER BY setrole) FROM pg_db_role_setting WHERE setdatabase=d.oid),'[]'::jsonb))::text FROM pg_database d WHERE datname=@name";
            async Task<string> Meta(string name){await using var cmd=new NpgsqlCommand(metadata,db);cmd.Parameters.AddWithValue("name",name);return (string)(await cmd.ExecuteScalarAsync())!;}
            if(await Meta(s.Database!)!=await Meta(x.Name))throw new InvalidOperationException("PRIVACY_RESTORE_DATABASE_METADATA_MISMATCH");
            await File.WriteAllTextAsync(Path.Combine(x.Directory,"native-restore.json"),JsonSerializer.Serialize(new{x.Id,Database=x.Name,Oid=x.oid,Source=s.Database,x.Sha256,ServerVersion=serverVersion,PgDumpVersion=dumpVersion,PgRestoreVersion=restoreVersion,NativeRestoreExit=0,DatabaseOwnerAclAndSettingsMatched=true},new JsonSerializerOptions{WriteIndented=true}));
            return x;
        }
        catch{await x.DisposeAsync();throw;}
    }
    private async Task<string> Native(NpgsqlConnectionStringBuilder source,string tool,string[] arguments)
    {
        var executable=Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_BIN")!,tool+".exe");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["PGHOST"]=source.Host;start.Environment["PGPORT"]=source.Port.ToString();start.Environment["PGUSER"]=source.Username;start.Environment["PGPASSWORD"]=source.Password;
        if(!arguments.Contains("--version"))start.ArgumentList.Add("--no-password");foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var p=Process.Start(start)??throw new InvalidOperationException("PRIVACY_NATIVE_RESTORE_NOT_STARTED");var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));}
        finally{if(!p.HasExited){p.Kill(entireProcessTree:true);await p.WaitForExitAsync();}await File.WriteAllTextAsync(Path.Combine(Directory,tool+".log"),(await stdout)+"\n"+await stderr);}
        if(p.ExitCode!=0)throw new InvalidOperationException("PRIVACY_NATIVE_"+tool.ToUpperInvariant()+"_FAILED: "+(await stderr));
        return await stdout;
    }
    public async ValueTask DisposeAsync()
    {
        if(!created)return;
        await using var db=new NpgsqlConnection(admin);await db.OpenAsync();
        await using var check=new NpgsqlCommand("SELECT oid::bigint,shobj_description(oid,'pg_database') FROM pg_database WHERE datname=@name",db);check.Parameters.AddWithValue("name",Name);
        await using(var r=await check.ExecuteReaderAsync())
        {if(!await r.ReadAsync()||r.GetInt64(0)!=oid||r.GetString(1)!="CP6-PRIVACY-RESTORE-CANDIDATE/v1/"+Id.ToString("D"))throw new InvalidOperationException("PRIVACY_RESTORE_OWNERSHIP_CHANGED");}
        await using var drop=new NpgsqlCommand($"DROP DATABASE \"{Name}\"",db);await drop.ExecuteNonQueryAsync();created=false;
        await File.WriteAllTextAsync(Path.Combine(Directory,"database-dropped.txt"),Name);
    }
}

using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
namespace LibraryManagement.Infrastructure.Repositories;

public sealed class ModuleAccessRepository(SqlConnectionFactory factory) : IModuleAccessRepository
{
    private static CommandDefinition Command(string name,object? args,CancellationToken ct) => new("library."+name,args,commandType:CommandType.StoredProcedure,cancellationToken:ct);
    private async Task<T> Run<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch(SqlException ex) when(ex.Number==50001) { throw new BusinessException(ex.Message); }
        catch(SqlException ex) when(ex.Number is 2601 or 2627) { throw new BusinessException("A duplicate controller or permission entry was submitted."); }
        catch(SqlException ex) when(ex.Number is 1205 or 547 or 515) { throw new BusinessException("Permission data changed or is invalid. Reload and try again."); }
    }
    public async Task<IReadOnlyList<ModulePath>> ModulesAsync(CancellationToken ct)
    {
        await using var sql=await factory.OpenAsync(ct);return (await sql.QueryAsync<ModulePath>(Command("Modules_List",null,ct))).AsList();
    }
    public Task<int> SaveModuleAsync(ModulePath m,string actor,CancellationToken ct) => Run(async()=>
    {
        await using var sql=await factory.OpenAsync(ct);return await sql.QuerySingleAsync<int>(Command("Modules_Save",new {m.Id,m.DisplayName,m.ControllerName,m.SortOrder,m.IsActive,m.Revision,m.IsRead,m.IsAdd,m.IsEdit,m.IsDelete,m.IsExport,m.IsImport,m.IsApproval1,m.IsApproval2,m.ParentId,m.IsLeafNode,m.LabelId,ActorId=actor},ct));
    });
    public Task DeleteModuleAsync(int id,int revision,string actor,CancellationToken ct) => Run(async()=>
    { await using var sql=await factory.OpenAsync(ct);return await sql.ExecuteAsync(Command("Modules_Delete",new {Id=id,Revision=revision,ActorId=actor},ct)); });
    public async Task<UserModuleAccess?> UserAsync(string userId,CancellationToken ct)
    {
        await using var sql=await factory.OpenAsync(ct);using var rows=await sql.QueryMultipleAsync(Command("ModuleAccess_Get",new {UserId=userId},ct));
        var user=await rows.ReadSingleOrDefaultAsync<UserModuleAccess>();var grants=(await rows.ReadAsync<UserModuleGrant>()).AsList();if(user is not null)user.Grants=grants;return user;
    }
    public Task SaveUserAsync(UserModuleAccess model,string actor,CancellationToken ct) => Run(async()=>
    {
        await using var sql=await factory.OpenAsync(ct);return await sql.ExecuteAsync(Command("ModuleAccess_Save",new {model.UserId,model.UserRevision,model.Revision,Grants=JsonSerializer.Serialize(model.Grants),ActorId=actor},ct));
    });
    public async Task<RoleModuleAccess?> RoleAsync(string role,CancellationToken ct)
    {
        await using var sql=await factory.OpenAsync(ct);using var rows=await sql.QueryMultipleAsync(Command("RoleAccess_Get",new{Role=role},ct));
        var profile=await rows.ReadSingleOrDefaultAsync<RoleModuleAccess>();var grants=(await rows.ReadAsync<UserModuleGrant>()).AsList();if(profile is not null)profile.Grants=grants;return profile;
    }
    public Task SaveRoleAsync(RoleModuleAccess model,string actor,CancellationToken ct)=>Run(async()=>
    {await using var sql=await factory.OpenAsync(ct);return await sql.ExecuteAsync(Command("RoleAccess_Save",new{model.Role,model.Revision,Grants=JsonSerializer.Serialize(model.Grants),ActorId=actor},ct));});
}

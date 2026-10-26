using CamGuard.Api.Common.Data;
using Microsoft.EntityFrameworkCore;

// Copia cámaras, grabaciones, eventos y ajustes de la base SQL Server de CamGuard a Postgres,
// conservando los Ids (las claves de R2 y los enlaces de los avisos siguen valiendo).
// Uso: dotnet run --project tools/CamGuard.DbMigrator -- --from "<SQL Server>" --to "<Postgres>" [--force]

const int BatchSize = 500;

var from = Arg("--from");
var to = Arg("--to");
var force = args.Contains("--force");
if (from is null || to is null)
{
    Console.Error.WriteLine("Uso: --from \"<cadena SQL Server>\" --to \"<cadena Postgres>\" [--force]");
    return 2;
}

await using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(from).Options);
await using var target = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(to).Options);

Console.WriteLine("Aplicando migraciones en Postgres…");
await target.Database.MigrateAsync();

if (await target.Cameras.AnyAsync() || await target.Recordings.AnyAsync() || await target.CameraEvents.AnyAsync())
{
    if (!force)
    {
        Console.Error.WriteLine("Postgres ya tiene datos. Usa --force para vaciarlo y copiar de nuevo.");
        return 1;
    }

    Console.WriteLine("Vaciando Postgres (--force)…");
    await target.Database.ExecuteSqlRawAsync("""TRUNCATE "CameraEvents", "Recordings", "Cameras" RESTART IDENTITY CASCADE""");
}

await CopyAsync(source.Cameras.OrderBy(c => c.Id), c => c.DeviceInfoAt = c.DeviceInfoAt?.ToUniversalTime());
await CopyAsync(source.Recordings.OrderBy(r => r.Id), r =>
{
    r.Camera = null;
    r.StartedAt = r.StartedAt.ToUniversalTime();
    r.EndedAt = r.EndedAt?.ToUniversalTime();
});
await CopyAsync(source.CameraEvents.OrderBy(e => e.Id), e => e.OccurredAt = e.OccurredAt.ToUniversalTime());

// La migración ya sembró la fila única de ajustes: se actualiza con los valores del origen.
var settings = await source.Settings.AsNoTracking().SingleAsync(s => s.Id == AppSettings.SingletonId);
var targetSettings = await target.Settings.SingleAsync(s => s.Id == AppSettings.SingletonId);
target.Entry(targetSettings).CurrentValues.SetValues(settings);
await target.SaveChangesAsync();

// Los Ids se insertaron a mano: las identidades deben seguir desde el mayor copiado.
await target.Database.ExecuteSqlRawAsync(
    """SELECT setval(pg_get_serial_sequence('"Cameras"', 'Id'), COALESCE((SELECT MAX("Id") FROM "Cameras"), 0) + 1, false)""");
await target.Database.ExecuteSqlRawAsync(
    """SELECT setval(pg_get_serial_sequence('"CameraEvents"', 'Id'), COALESCE((SELECT MAX("Id") FROM "CameraEvents"), 0) + 1, false)""");

var ok = await CheckAsync("Cámaras", source.Cameras, target.Cameras)
         & await CheckAsync("Grabaciones", source.Recordings, target.Recordings)
         & await CheckAsync("Eventos", source.CameraEvents, target.CameraEvents);

Console.WriteLine(ok ? "Migración completa." : "Los conteos no coinciden: revisa antes de retirar SQL Server.");
return ok ? 0 : 1;

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

async Task CopyAsync<T>(IQueryable<T> query, Action<T> normalize) where T : class
{
    var name = typeof(T).Name;
    var copied = 0;
    for (var skip = 0; ; skip += BatchSize)
    {
        var batch = await query.AsNoTracking().Skip(skip).Take(BatchSize).ToListAsync();
        if (batch.Count == 0) break;

        foreach (var item in batch) normalize(item);
        target.AddRange(batch);
        await target.SaveChangesAsync();
        target.ChangeTracker.Clear();

        copied += batch.Count;
        Console.WriteLine($"{name}: {copied}");
    }
}

static async Task<bool> CheckAsync<T>(string label, IQueryable<T> source, IQueryable<T> target)
{
    var (s, t) = (await source.CountAsync(), await target.CountAsync());
    Console.WriteLine($"{label}: SQL Server {s} / Postgres {t}");
    return s == t;
}

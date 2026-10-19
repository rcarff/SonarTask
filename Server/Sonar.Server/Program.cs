using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var dataRoot = Environment.GetEnvironmentVariable("SONAR_DATA_ROOT") ?? "/data";
var experimentRoot = Path.Combine(dataRoot, "experiments");
var resultRoot = Path.Combine(dataRoot, "results");
var configRoot = Path.Combine(dataRoot, "config");
Directory.CreateDirectory(experimentRoot);
Directory.CreateDirectory(resultRoot);
Directory.CreateDirectory(configRoot);
var authFile = Path.Combine(configRoot, "auth.json");
var dbFile = Path.Combine(configRoot, "sonar.db");
const string CsvHeader = "Time,TotalElapsedTime,ExpElapsedTime,Phase,PhaseElapsedTimeSec,EventType,Signal_Type,Signal_ID,Signal_Bearing,Signal_Width,Signal_Alpha,BTH_SelectedSignals,BTH_SelectedBearing,Class_Selected,Class_Types,Class_Correct,Class_Confidence,Class_Feedback,Alert_Type,Alert_Text,Alert_Bearing,Alert_SignalId,RunId";

if (args.Length > 0 && (args[0] == "reset-password" || args[0] == "init-password"))
{
    Console.Write("New operator password: ");
    var p = ReadPassword();
    Console.WriteLine();
    if (p.Length < 12) { Console.Error.WriteLine("Password must be at least 12 characters."); return; }
    await AuthStore.Save(authFile, p);
    Console.WriteLine("Operator password updated. Existing elevated settings tokens are invalidated.");
    return;
}

await Db.Init(dbFile);
await ImportLegacyExternalStudies(dbFile, configRoot, experimentRoot);

var builder = WebApplication.CreateBuilder(args);
const long MaxExperimentZipBytes = 64L * 1024 * 1024;
const long MaxMultipartBodyBytes = 70L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 72L * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxMultipartBodyBytes);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "sonar_session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("login", x =>
{
    x.PermitLimit = 8;
    x.Window = TimeSpan.FromMinutes(5);
    x.QueueLimit = 0;
    x.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
}));

var app = builder.Build();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapPost("/auth/login", async (HttpContext c, PasswordRequest r) =>
{
    if (!await AuthStore.Verify(authFile, r.password))
        return Results.Json(new { ok = false, message = "Invalid password." }, statusCode: 401);
    var claims = new[] { new Claim(ClaimTypes.Name, "operator"), new Claim(ClaimTypes.Role, "operator") };
    await c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { ok = true, message = "Signed in." });
}).RequireRateLimiting("login");

app.MapPost("/auth/reauth", async (HttpContext c, PasswordRequest r) =>
{
    if (!IsOperator(c)) return Results.Unauthorized();
    if (!await AuthStore.Verify(authFile, r.password))
        return Results.Json(new { ok = false, message = "Invalid password." }, statusCode: 401);
    var a = await AuthStore.Load(authFile);
    c.Response.Cookies.Append("sonar_admin", a.AdminKey, new CookieOptions
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
        Expires = DateTimeOffset.UtcNow.AddMinutes(15)
    });
    return Results.Ok(new { ok = true, message = "Settings unlocked for 15 minutes." });
}).RequireAuthorization();

app.MapPost("/auth/change-password", async (HttpContext c, ChangePasswordRequest r) =>
{
    if (!Admin(c, authFile) || !await AuthStore.Verify(authFile, r.currentPassword))
        return Results.Json(new { ok = false, message = "Re-authentication required." }, statusCode: 401);
    if ((r.newPassword ?? "").Length < 12)
        return Results.BadRequest(new { ok = false, message = "New password must be at least 12 characters." });
    await AuthStore.Save(authFile, r.newPassword);
    c.Response.Cookies.Delete("sonar_admin");
    return Results.Ok(new { ok = true, message = "Password changed. Re-open Settings to authenticate again." });
}).RequireAuthorization();

// External participant launch. Provider participant/study identifiers select a server-side
// mapping; the browser is never trusted to choose its production experiment package.
app.MapPost("/external/launch", async (HttpContext c, ExternalLaunchRequest request) =>
{
    var provider = NormalizeProvider(request.provider);
    var participant = (request.participantId ?? "").Trim();
    var providerStudyId = (request.providerStudyId ?? "").Trim();
    if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(participant) || string.IsNullOrWhiteSpace(providerStudyId))
        return Results.BadRequest(new { ok = false, message = "Provider, participant ID, and provider study/project ID are required." });

    var study = await Db.GetExternalStudy(dbFile, provider, providerStudyId);
    if (study == null)
        return Results.NotFound(new { ok = false, message = $"No SONAR External Study mapping matches provider '{provider}' and study/project ID '{providerStudyId}'. Save the mapping in Settings before testing this URL." });
    if (!study.enabled)
        return Results.Json(new { ok = false, message = "The matching SONAR External Study is disabled. Check Enabled in Settings and save the study before launching or generating a test URL." }, statusCode: 409);

    bool testMode = !string.IsNullOrWhiteSpace(request.testToken);
    if (testMode && !await Db.ValidateExternalTestToken(dbFile, study.id, request.testToken!))
        return Results.Json(new { ok = false, message = "The External Study test token is invalid or expired. Generate a new test URL from Settings." }, statusCode: 401);

    var assignment = await Db.GetOrCreateExternalAssignment(dbFile, study, participant, request.sessionId ?? "", request.assignmentId ?? "", testMode);
    var reply = BuildExternalReply(study, assignment, testMode);

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, "external:" + participant),
        new Claim(ClaimTypes.Role, "participant"),
        new Claim("sonar_external_study_id", study.id),
        new Claim("sonar_participant_id", participant),
        new Claim("sonar_provider", provider),
        new Claim("sonar_provider_study_id", providerStudyId),
        new Claim("sonar_external_test", testMode ? "1" : "0")
    };
    await c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(reply);
});

app.MapGet("/external/progress", async (HttpContext c) =>
{
    if (!TryParticipantClaims(c, out var studyId, out var participantId)) return Results.Unauthorized();
    var study = await Db.GetExternalStudyById(dbFile, studyId);
    bool testMode = IsExternalTest(c);
    var assignment = await Db.GetExternalAssignment(dbFile, studyId, participantId, testMode);
    if (study == null || assignment == null) return Results.NotFound(new { ok = false, message = "External-study assignment was not found." });
    return Results.Ok(BuildExternalReply(study, assignment, testMode));
}).RequireAuthorization();

// Experiment access is role-aware. Operators can access all packages; an external participant
// may access only the experiment that is current in their persistent assigned sequence.
app.MapGet("/experiments", async (HttpContext c) =>
{
    if (IsOperator(c)) return Results.Ok(ExperimentFiles.List(experimentRoot));
    var package = await CurrentParticipantPackage(c, dbFile);
    if (package == null) return Results.Unauthorized();
    return Results.Ok(ExperimentFiles.List(experimentRoot).Where(x => string.Equals(x.packageName, package, StringComparison.OrdinalIgnoreCase)).ToArray());
}).RequireAuthorization();

app.MapGet("/experiments/{package}/definition", async (HttpContext c, string package) =>
{
    if (!await CanAccessPackage(c, dbFile, package)) return Results.Forbid();
    var f = SafeExperimentFile(experimentRoot, package, "experiment.json");
    return File.Exists(f) ? Results.File(f, "application/json") : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/experiments/{package}/assets/{**asset}", async (HttpContext c, string package, string asset) =>
{
    if (!await CanAccessPackage(c, dbFile, package)) return Results.Forbid();
    var f = SafeExperimentFile(experimentRoot, package, asset);
    return File.Exists(f) ? Results.File(f, ContentType(f)) : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/results/completed", async (HttpContext c, string subject, string experiment) =>
{
    if (!IsOperator(c)) return Results.Forbid();
    return Results.Ok(new { completed = await Db.HasCompleted(dbFile, subject, experiment) });
}).RequireAuthorization();

app.MapPost("/results/runs", async (HttpContext c, RunStartRequest start) =>
{
    var m = start.metadata;
    if (string.IsNullOrWhiteSpace(m.RunId)) return Results.BadRequest("RunId is required.");
    var hash = Sha256Text(start.definitionJson ?? "");
    if (!string.Equals(hash, m.ExperimentDefinitionHash, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("ExperimentDefinitionHash does not match submitted experiment snapshot.");
    try
    {
        using var parsed = JsonDocument.Parse(start.definitionJson);
        if (!parsed.RootElement.TryGetProperty("Name", out var en) || en.GetString() != m.ExperimentName)
            return Results.BadRequest("Experiment snapshot name does not match metadata.");
    }
    catch (Exception e) { return Results.BadRequest("Invalid experiment snapshot: " + e.Message); }

    RunExternalContext? externalContext = null;
    if (IsParticipant(c))
    {
        if (!TryParticipantClaims(c, out var studyId, out var participantId)) return Results.Unauthorized();
        bool testMode = IsExternalTest(c);
        var study = await Db.GetExternalStudyById(dbFile, studyId);
        var assignment = await Db.GetExternalAssignment(dbFile, studyId, participantId, testMode);
        if (study == null || assignment == null) return Results.Unauthorized();
        var progress = BuildExternalReply(study, assignment, testMode);
        if (progress.completed || string.IsNullOrWhiteSpace(progress.experimentPackage))
            return Results.Conflict(new { message = "This external-study assignment is already complete." });
        if (!string.Equals(progress.experimentPackage, m.ExperimentPackage, StringComparison.OrdinalIgnoreCase))
            return Results.Forbid();
        if (!string.Equals(participantId, m.SubjectId, StringComparison.Ordinal))
            return Results.BadRequest("SubjectId does not match the authenticated external participant.");

        // Overwrite external metadata with server-authoritative assignment values.
        m = m with
        {
            LaunchMode = "External",
            RecruitmentProvider = study.provider,
            ProviderParticipantId = participantId,
            ProviderStudyId = study.providerStudyId,
            ProviderSessionId = assignment.sessionId,
            ProviderAssignmentId = assignment.providerAssignmentId,
            ExternalStudyId = study.id,
            AssignmentMode = study.assignmentMode,
            AssignmentGroup = assignment.groupName,
            ExperimentSequence = assignment.experimentSequence.ToArray(),
            SequenceIndex = assignment.currentIndex + 1,
            SequenceCount = assignment.experimentSequence.Count,
            ExternalTestMode = testMode
        };
        externalContext = new RunExternalContext(m.RunId, study.id, participantId, assignment.currentIndex + 1, testMode);
    }
    else if (!IsOperator(c)) return Results.Unauthorized();

    var stem = Safe(m.SubjectId) + "__" + Safe(m.ExperimentPackage) + "__" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "__" + m.RunId[..Math.Min(8, m.RunId.Length)];
    var csv = Path.Combine(resultRoot, stem + ".csv");
    var meta = Path.Combine(resultRoot, stem + ".meta.json");
    var snap = Path.Combine(resultRoot, stem + ".experiment.json");
    await File.WriteAllTextAsync(csv, CsvHeader + Environment.NewLine, new UTF8Encoding(false));
    m = m with { Completed = false, TerminationReason = "Active" };
    await File.WriteAllTextAsync(meta, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(snap, start.definitionJson ?? "", new UTF8Encoding(false));
    await Db.Start(dbFile, m, Path.GetFileName(csv), Path.GetFileName(meta));
    if (externalContext != null) await Db.AddRunExternalContext(dbFile, externalContext);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapPost("/results/runs/{runId}/heartbeat", async (HttpContext c, string runId) =>
{
    if (!await CanAccessRun(c, dbFile, runId)) return Results.Forbid();
    await Db.Heartbeat(dbFile, runId);
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/results/runs/{runId}/events", async (HttpContext c, string runId, EventEnvelope env) =>
{
    if (!await CanAccessRun(c, dbFile, runId)) return Results.Forbid();
    var row = await Db.GetRun(dbFile, runId);
    if (row is null) return Results.NotFound();
    if (await Db.SequenceExists(dbFile, runId, env.sequence)) return Results.Ok(new { duplicate = true });
    var path = Path.Combine(resultRoot, row.Value.csv);
    await File.AppendAllTextAsync(path, ToCsv(env.@event, runId) + Environment.NewLine, new UTF8Encoding(false));
    await Db.AddSequence(dbFile, runId, env.sequence);
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/results/runs/{runId}/finish", async (HttpContext c, string runId, FinishRequest f) =>
{
    if (!await CanAccessRun(c, dbFile, runId)) return Results.Forbid();
    var row = await Db.GetRun(dbFile, runId);
    if (row is null) return Results.NotFound();
    await Db.Finish(dbFile, runId, f.completed, f.terminationReason, f.endedUtc);
    await RewriteMeta(Path.Combine(resultRoot, row.Value.meta), f.completed, f.terminationReason, f.endedUtc);
    if (f.completed)
    {
        var context = await Db.GetRunExternalContext(dbFile, runId);
        if (context != null)
            await Db.CompleteExternalStep(dbFile, context.externalStudyId, context.participantId, context.sequenceIndexOneBased, context.testMode);
    }
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// Password-protected administration endpoints.
app.MapGet("/admin/experiments", (HttpContext c) =>
    Admin(c, authFile) ? Results.Ok(ExperimentFiles.List(experimentRoot)) : Results.Unauthorized()).RequireAuthorization();

app.MapMethods("/admin/experiments/storage-status", new[] { "GET", "POST" }, (HttpContext c) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    try
    {
        Directory.CreateDirectory(experimentRoot);
        var test = Path.Combine(experimentRoot, ".sonar-write-test-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(test, "ok"); File.Delete(test);
        return Results.Ok(new { writable = true, message = "Experiment storage is writable." });
    }
    catch (Exception e) { return Results.Ok(new { writable = false, message = e.Message }); }
}).RequireAuthorization();

app.MapPost("/admin/experiments/upload", async (HttpContext c, IFormFile file) =>
{
    if (!Admin(c, authFile))
        return Results.Json(new { ok = false, message = "Settings authorization expired. Re-enter the operator password and try again." }, statusCode: 401);
    try
    {
        if (file.Length <= 0) return Results.BadRequest(new { ok = false, message = "The selected ZIP is empty." });
        if (file.Length > MaxExperimentZipBytes) return Results.Json(new { ok = false, message = "ZIP exceeds the 64 MiB experiment upload limit." }, statusCode: 413);
        await using var ms = new MemoryStream(); await file.CopyToAsync(ms);
        await InstallZip(ms.ToArray(), file.FileName, experimentRoot);
        return Results.Ok(new { ok = true, message = "Experiment uploaded." });
    }
    catch (UnauthorizedAccessException e) { return Results.Json(new { ok = false, message = "The server cannot write to the Experiments directory. Check the Deploy/Experiments bind-mount permissions. " + e.Message }, statusCode: 500); }
    catch (IOException e) { return Results.Json(new { ok = false, message = "Experiment storage I/O error: " + e.Message }, statusCode: 500); }
    catch (Exception e) { return Results.BadRequest(new { ok = false, message = e.Message }); }
}).DisableAntiforgery().RequireAuthorization();

app.MapPost("/admin/experiments/upload-base64", async (HttpContext c, UploadBase64 u) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    try { var bytes = Convert.FromBase64String(u.base64); await InstallZip(bytes, u.fileName, experimentRoot); return Results.Ok(new { ok = true }); }
    catch (Exception e) { return Results.BadRequest(new { ok = false, message = e.Message }); }
}).RequireAuthorization();

app.MapDelete("/admin/experiments/{package}", async (HttpContext c, string package) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    var studies = await Db.ListExternalStudies(dbFile);
    var referencedBy = studies
        .Where(s => (s.groups ?? new List<ExternalStudyGroupDto>()).Any(g => (g.experiments ?? new List<string>()).Any(x => string.Equals(x, package, StringComparison.OrdinalIgnoreCase))))
        .Select(s => $"{s.provider}:{s.providerStudyId}")
        .ToArray();
    if (referencedBy.Length > 0)
        return Results.Conflict(new { ok = false, message = "Experiment package is referenced by External Study mapping(s): " + string.Join(", ", referencedBy) + ". Update/disable the mappings before deleting the package." });
    var dir = SafePackageDir(experimentRoot, package);
    if (!Directory.Exists(dir)) return Results.NotFound();
    Directory.Delete(dir, true);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapGet("/admin/results", async (HttpContext c, string? filter) =>
    Admin(c, authFile) ? Results.Ok(await Db.Results(dbFile, filter ?? "")) : Results.Unauthorized()).RequireAuthorization();

app.MapGet("/admin/results/file/{fileName}", (HttpContext c, string fileName) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    var f = SafeResultFile(resultRoot, fileName);
    return File.Exists(f) ? Results.File(f, "application/octet-stream", Path.GetFileName(f)) : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/admin/results/download", (HttpContext c, string? filter) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    var files = Directory.GetFiles(resultRoot).Where(f => Path.GetFileName(f).Contains(filter ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
    return ZipFiles(files, "sonar-results.zip");
}).RequireAuthorization();

var tokens = new Dictionary<string, (DateTime expires, string[] files)>();
app.MapPost("/admin/results/download-selected", (HttpContext c, SelectedFiles s) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    var fs = s.files.SelectMany(x =>
    {
        var safe = SafeResultFile(resultRoot, x);
        var stem = Path.Combine(resultRoot, Path.GetFileNameWithoutExtension(safe));
        return new[] { safe, stem + ".meta.json", stem + ".experiment.json" };
    }).Where(File.Exists).Distinct().ToArray();
    var token = Guid.NewGuid().ToString("N");
    lock (tokens) tokens[token] = (DateTime.UtcNow.AddMinutes(2), fs);
    return Results.Json(token);
}).RequireAuthorization();

app.MapGet("/admin/results/download-token/{token}", (HttpContext c, string token) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    (DateTime expires, string[] files) t;
    lock (tokens)
    {
        if (!tokens.TryGetValue(token, out t) || t.expires < DateTime.UtcNow) return Results.NotFound();
        tokens.Remove(token);
    }
    return ZipFiles(t.files, "sonar-selected-results.zip");
}).RequireAuthorization();

app.MapGet("/admin/external-studies", async (HttpContext c) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    return Results.Ok(await Db.ListExternalStudies(dbFile));
}).RequireAuthorization();

app.MapPost("/admin/external-studies", async (HttpContext c, ExternalStudyConfigDto config) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    config = NormalizeStudy(config);
    var errors = ValidateExternalStudy(config, experimentRoot);
    if (errors.Count > 0) return Results.BadRequest(new { ok = false, message = string.Join("\n", errors) });

    var existing = string.IsNullOrWhiteSpace(config.id) ? null : await Db.GetExternalStudyById(dbFile, config.id);
    if (existing != null && await Db.ExternalStudyAssignmentCount(dbFile, existing.id) > 0 && AllocationDefinitionChanged(existing, config))
        return Results.Conflict(new { ok = false, message = "This external study already has participant assignments. Provider/study ID, assignment mode, and groups/sequences are locked. You may still change Enabled, Auto Start, Show Instructions, or Completion URL." });

    try
    {
        await Db.UpsertExternalStudy(dbFile, config);
        return Results.Ok(new { ok = true, message = "External study saved." });
    }
    catch (SqliteException e) when (e.SqliteErrorCode == 19)
    {
        return Results.Conflict(new { ok = false, message = "Another External Study mapping already uses this provider Study/Project ID." });
    }
}).RequireAuthorization();

app.MapPost("/admin/external-studies/{id}/test-token", async (HttpContext c, string id) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    var study = await Db.GetExternalStudyById(dbFile, id);
    if (study == null) return Results.NotFound(new { ok = false, message = "External Study was not found." });
    var token = await Db.CreateExternalTestToken(dbFile, id, TimeSpan.FromHours(24));
    return Results.Ok(new { ok = true, token = token.token, expiresUtc = token.expiresUtc });
}).RequireAuthorization();

app.MapPost("/admin/external-studies/{id}/reset-test-data", async (HttpContext c, string id) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    var study = await Db.GetExternalStudyById(dbFile, id);
    if (study == null) return Results.NotFound(new { ok = false, message = "External Study was not found." });
    var reset = await Db.ResetExternalStudyData(dbFile, resultRoot, id, true, true);
    return Results.Ok(new { ok = true, assignmentsDeleted = reset.assignmentsDeleted, runsDeleted = reset.runsDeleted, message = "Test assignments and test result files cleared." });
}).RequireAuthorization();

app.MapPost("/admin/external-studies/{id}/reset-production-data", async (HttpContext c, string id, ResetStudyDataRequest request) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    var study = await Db.GetExternalStudyById(dbFile, id);
    if (study == null) return Results.NotFound(new { ok = false, message = "External Study was not found." });
    var reset = await Db.ResetExternalStudyData(dbFile, resultRoot, id, false, request.deleteResults);
    return Results.Ok(new { ok = true, assignmentsDeleted = reset.assignmentsDeleted, runsDeleted = reset.runsDeleted, message = request.deleteResults ? "Production assignments and associated result files reset." : "Production assignment counts reset; result files retained." });
}).RequireAuthorization();

app.MapDelete("/admin/external-studies/{id}", async (HttpContext c, string id) =>
{
    if (!Admin(c, authFile)) return Results.Unauthorized();
    if (await Db.ExternalStudyAssignmentCount(dbFile, id) > 0)
        return Results.Conflict(new { ok = false, message = "This external study has participant assignments and cannot be deleted. Disable it instead." });
    await Db.DeleteExternalStudy(dbFile, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), app.Lifetime.ApplicationStopping);
            var stale = await Db.MarkStale(dbFile, TimeSpan.FromSeconds(75));
            foreach (var x in stale)
                await RewriteMeta(Path.Combine(resultRoot, x), false, "BrowserDisconnected", DateTime.UtcNow.ToString("O"));
        }
        catch (OperationCanceledException) { break; }
        catch (Exception e) { Console.Error.WriteLine("Run reaper: " + e.Message); }
    }
}));

app.Run();

static bool IsOperator(HttpContext c) => c.User.Identity?.IsAuthenticated == true && c.User.IsInRole("operator");
static bool IsParticipant(HttpContext c) => c.User.Identity?.IsAuthenticated == true && c.User.IsInRole("participant");
static bool IsExternalTest(HttpContext c) => string.Equals(c.User.FindFirstValue("sonar_external_test"), "1", StringComparison.Ordinal);

static bool TryParticipantClaims(HttpContext c, out string studyId, out string participantId)
{
    studyId = c.User.FindFirstValue("sonar_external_study_id") ?? "";
    participantId = c.User.FindFirstValue("sonar_participant_id") ?? "";
    return IsParticipant(c) && !string.IsNullOrWhiteSpace(studyId) && !string.IsNullOrWhiteSpace(participantId);
}

static async Task<string?> CurrentParticipantPackage(HttpContext c, string dbFile)
{
    if (!TryParticipantClaims(c, out var studyId, out var participantId)) return null;
    var study = await Db.GetExternalStudyById(dbFile, studyId);
    bool testMode = IsExternalTest(c);
    var assignment = await Db.GetExternalAssignment(dbFile, studyId, participantId, testMode);
    if (study == null || assignment == null) return null;
    var reply = BuildExternalReply(study, assignment, testMode);
    return reply.completed ? null : reply.experimentPackage;
}

static async Task<bool> CanAccessPackage(HttpContext c, string dbFile, string package)
{
    if (IsOperator(c)) return true;
    var current = await CurrentParticipantPackage(c, dbFile);
    return current != null && string.Equals(current, package, StringComparison.OrdinalIgnoreCase);
}

static async Task<bool> CanAccessRun(HttpContext c, string dbFile, string runId)
{
    if (IsOperator(c)) return true;
    if (!TryParticipantClaims(c, out var studyId, out var participantId)) return false;
    return await Db.RunBelongsToExternalParticipant(dbFile, runId, studyId, participantId, IsExternalTest(c));
}

static bool Admin(HttpContext c, string authFile)
{
    if (!IsOperator(c) || !c.Request.Cookies.TryGetValue("sonar_admin", out var token)) return false;
    try
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(AuthStore.Load(authFile).GetAwaiter().GetResult().AdminKey));
    }
    catch { return false; }
}

static string NormalizeProvider(string provider) => (provider ?? "").Trim().ToLowerInvariant() switch
{
    "cloudresearch" or "cloudresearchconnect" or "cloudresearch connect" => "connect",
    "sona systems" => "sona",
    var x => x
};

static string NormalizeAssignmentMode(string mode) => (mode ?? "").Trim().ToLowerInvariant() switch
{
    "random" => "Random",
    "balancedrandom" or "balanced random" => "BalancedRandom",
    "counterbalanced" or "counterbalance" => "Counterbalanced",
    _ => "Fixed"
};

static ExternalStudyConfigDto NormalizeStudy(ExternalStudyConfigDto c)
{
    c.id = string.IsNullOrWhiteSpace(c.id) ? Guid.NewGuid().ToString("N") : c.id.Trim();
    c.provider = NormalizeProvider(c.provider);
    c.providerStudyId = (c.providerStudyId ?? "").Trim();
    c.assignmentMode = NormalizeAssignmentMode(c.assignmentMode);
    c.completionUrl = (c.completionUrl ?? "").Trim();
    c.groups ??= new List<ExternalStudyGroupDto>();
    for (int i = 0; i < c.groups.Count; i++)
    {
        var g = c.groups[i] ?? new ExternalStudyGroupDto();
        c.groups[i] = g;
        g.id = string.IsNullOrWhiteSpace(g.id) ? Guid.NewGuid().ToString("N") : g.id.Trim();
        g.name = string.IsNullOrWhiteSpace(g.name) ? "Group " + (i + 1) : g.name.Trim();
        if (g.weight < 1) g.weight = 1;
        g.assignedCount = 0;
        g.completedCount = 0;
        g.experiments ??= new List<string>();
        g.experiments = g.experiments.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
    }
    c.assignedCount = 0;
    c.completedCount = 0;
    return c;
}

static List<string> ValidateExternalStudy(ExternalStudyConfigDto c, string experimentRoot)
{
    var errors = new List<string>();
    if (!(c.provider is "prolific" or "connect" or "sona" or "generic")) errors.Add("Provider must be Prolific, Connect, SONA, or Generic.");
    if (string.IsNullOrWhiteSpace(c.providerStudyId)) errors.Add("Provider Study / Project ID is required.");
    if (c.groups == null || c.groups.Count == 0) errors.Add("At least one group/sequence is required.");
    if (c.assignmentMode == "Fixed" && c.groups.Count != 1) errors.Add("Fixed assignment requires exactly one group.");
    if ((c.assignmentMode == "Random" || c.assignmentMode == "BalancedRandom" || c.assignmentMode == "Counterbalanced") && c.groups.Count < 2)
        errors.Add(c.assignmentMode + " requires at least two groups/sequences.");
    if (!string.IsNullOrWhiteSpace(c.completionUrl))
    {
        if (!Uri.TryCreate(c.completionUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            errors.Add("Completion URL must be an absolute HTTP/HTTPS URL.");
    }
    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var g in c.groups ?? new List<ExternalStudyGroupDto>())
    {
        if (!ids.Add(g.id)) errors.Add("Group IDs must be unique.");
        if (g.weight < 1) errors.Add($"Group '{g.name}' weight must be at least 1.");
        if (g.experiments == null || g.experiments.Count == 0) errors.Add($"Group '{g.name}' must contain at least one experiment package.");
        foreach (var package in g.experiments ?? new List<string>())
        {
            try
            {
                var dir = SafePackageDir(experimentRoot, package);
                if (!File.Exists(Path.Combine(dir, "experiment.json"))) errors.Add($"Group '{g.name}' references missing experiment package '{package}'.");
            }
            catch { errors.Add($"Group '{g.name}' contains invalid experiment package name '{package}'."); }
        }
    }
    return errors;
}

static bool AllocationDefinitionChanged(ExternalStudyConfigDto old, ExternalStudyConfigDto current)
{
    if (!string.Equals(old.provider, current.provider, StringComparison.OrdinalIgnoreCase)) return true;
    if (!string.Equals(old.providerStudyId, current.providerStudyId, StringComparison.Ordinal)) return true;
    if (!string.Equals(NormalizeAssignmentMode(old.assignmentMode), NormalizeAssignmentMode(current.assignmentMode), StringComparison.Ordinal)) return true;
    var a = JsonSerializer.Serialize(old.groups ?? new List<ExternalStudyGroupDto>());
    var b = JsonSerializer.Serialize(current.groups ?? new List<ExternalStudyGroupDto>());
    return !string.Equals(a, b, StringComparison.Ordinal);
}

static ExternalLaunchReply BuildExternalReply(ExternalStudyConfigDto study, ExternalAssignmentRow assignment, bool testMode = false)
{
    var count = assignment.experimentSequence.Count;
    var completed = string.Equals(assignment.status, "COMPLETED", StringComparison.OrdinalIgnoreCase) || assignment.currentIndex >= count;
    var package = completed || count == 0 ? "" : assignment.experimentSequence[assignment.currentIndex];
    return new ExternalLaunchReply
    {
        ok = true,
        message = completed ? "External study sequence is complete." : "External-study assignment ready.",
        provider = study.provider,
        participantId = assignment.participantId,
        providerStudyId = study.providerStudyId,
        sessionId = assignment.sessionId,
        assignmentId = assignment.providerAssignmentId,
        externalStudyId = study.id,
        assignmentMode = study.assignmentMode,
        assignmentGroup = assignment.groupName,
        experimentSequence = assignment.experimentSequence,
        sequenceIndex = assignment.currentIndex,
        sequenceCount = count,
        experimentPackage = package,
        completionUrl = testMode ? "" : study.completionUrl,
        autoStart = study.autoStart,
        showInstructions = study.showInstructions,
        completed = completed,
        testMode = testMode
    };
}

static async Task ImportLegacyExternalStudies(string dbFile, string configRoot, string experimentRoot)
{
    // fixed31/fixed32 used a single-experiment mapping model. If one of the legacy
    // JSON files exists and the new SQLite table is empty, import each mapping as a
    // Fixed group. The legacy file is left untouched as a backup.
    if ((await Db.ListExternalStudies(dbFile)).Count > 0) return;
    var candidates = new[] {
        Path.Combine(configRoot, "external-studies.json"),
        Path.Combine(configRoot, "external_studies.json"),
        Path.Combine(configRoot, "externalStudies.json")
    };
    var legacy = candidates.FirstOrDefault(File.Exists);
    if (legacy == null) return;
    try
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(legacy));
        JsonElement rows = doc.RootElement;
        if (rows.ValueKind == JsonValueKind.Object && TryLegacyProperty(rows, "studies", out var nested)) rows = nested;
        if (rows.ValueKind != JsonValueKind.Array) return;
        int imported = 0;
        foreach (var item in rows.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var provider = NormalizeProvider(LegacyString(item, "provider"));
            var studyId = LegacyString(item, "providerStudyId", "studyId", "projectId");
            var package = LegacyString(item, "experimentPackage", "experiment", "packageName");
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(studyId) || string.IsNullOrWhiteSpace(package)) continue;
            var c = NormalizeStudy(new ExternalStudyConfigDto {
                id = LegacyString(item, "id"),
                provider = provider,
                providerStudyId = studyId,
                assignmentMode = "Fixed",
                completionUrl = LegacyString(item, "completionUrl"),
                autoStart = LegacyBool(item, "autoStart", true),
                showInstructions = LegacyBool(item, "showInstructions", true),
                enabled = LegacyBool(item, "enabled", true),
                groups = new List<ExternalStudyGroupDto> {
                    new() { id = Guid.NewGuid().ToString("N"), name = "Group 1", weight = 1, experiments = new List<string> { package } }
                }
            });
            if (ValidateExternalStudy(c, experimentRoot).Count > 0) continue;
            await Db.UpsertExternalStudy(dbFile, c);
            imported++;
        }
        if (imported > 0) Console.WriteLine($"Imported {imported} legacy External Study mapping(s) from {Path.GetFileName(legacy)} as Fixed assignments.");
    }
    catch (Exception e) { Console.Error.WriteLine("Legacy External Study import skipped: " + e.Message); }
}

static bool TryLegacyProperty(JsonElement obj, string name, out JsonElement value)
{
    foreach (var p in obj.EnumerateObject())
        if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
    value = default; return false;
}

static string LegacyString(JsonElement obj, params string[] names)
{
    foreach (var name in names)
        if (TryLegacyProperty(obj, name, out var p) && p.ValueKind == JsonValueKind.String) return (p.GetString() ?? "").Trim();
    return "";
}

static bool LegacyBool(JsonElement obj, string name, bool fallback)
{
    if (!TryLegacyProperty(obj, name, out var p)) return fallback;
    if (p.ValueKind == JsonValueKind.True) return true;
    if (p.ValueKind == JsonValueKind.False) return false;
    if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var b)) return b;
    return fallback;
}

static string Safe(string s)
{
    var bad = Path.GetInvalidFileNameChars();
    return new string((s ?? "unknown").Select(c => bad.Contains(c) ? '_' : c).ToArray());
}

static string SafePackageDir(string root, string package)
{
    if (package.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || package.Contains("..")) throw new InvalidOperationException("Invalid package name.");
    var p = Path.GetFullPath(Path.Combine(root, package));
    if (!p.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid path.");
    return p;
}

static string SafeExperimentFile(string root, string package, string rel)
{
    var dir = SafePackageDir(root, package);
    var p = Path.GetFullPath(Path.Combine(dir, rel ?? ""));
    if (!p.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal) && p != dir) throw new InvalidOperationException("Asset path escapes package.");
    return p;
}

static string SafeResultFile(string root, string name) => Path.Combine(root, Path.GetFileName(name));
static string ContentType(string f) => Path.GetExtension(f).ToLowerInvariant() switch
{
    ".json" => "application/json", ".md" => "text/markdown; charset=utf-8", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".wav" => "audio/wav", ".mp3" => "audio/mpeg", _ => "application/octet-stream"
};

static IResult ZipFiles(string[] files, string download)
{
    var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
    {
        foreach (var f in files)
        {
            var e = zip.CreateEntry(Path.GetFileName(f), CompressionLevel.Fastest);
            using var dst = e.Open(); using var src = File.OpenRead(f); src.CopyTo(dst);
        }
    }
    ms.Position = 0;
    return Results.File(ms, "application/zip", download);
}

static async Task InstallZip(byte[] data, string original, string root)
{
    if (data.Length > 64 * 1024 * 1024) throw new InvalidOperationException("ZIP exceeds 64 MB upload limit.");
    var tmp = Path.Combine(Path.GetTempPath(), "sonar-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
    try
    {
        using var ms = new MemoryStream(data); using var z = new ZipArchive(ms, ZipArchiveMode.Read);
        long expanded = 0; int count = 0;
        foreach (var e in z.Entries)
        {
            if (++count > 2000) throw new InvalidOperationException("ZIP contains too many files.");
            expanded += e.Length; if (expanded > 256L * 1024 * 1024) throw new InvalidOperationException("Expanded ZIP exceeds 256 MB.");
            if (string.IsNullOrEmpty(e.Name)) continue;
            var target = Path.GetFullPath(Path.Combine(tmp, e.FullName));
            if (!target.StartsWith(Path.GetFullPath(tmp) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("ZIP contains an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var dst = File.Create(target); await using var src = e.Open(); await src.CopyToAsync(dst);
        }
        var jsons = Directory.GetFiles(tmp, "experiment.json", SearchOption.AllDirectories);
        if (jsons.Length != 1) throw new InvalidOperationException("ZIP must contain exactly one experiment.json.");
        var packageDir = Path.GetDirectoryName(jsons[0])!;
        if (Path.GetFullPath(packageDir) == Path.GetFullPath(tmp)) throw new InvalidOperationException("ZIP must contain one top-level experiment package directory.");
        var package = Path.GetFileName(packageDir);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsons[0]));
        var errors = ExperimentFiles.ValidatePackage(doc.RootElement, packageDir);
        if (errors.Count > 0) throw new InvalidOperationException("Experiment validation failed:\n - " + string.Join("\n - ", errors));
        var final = SafePackageDir(root, package);
        var stagingBase = Path.Combine(root, ".staging", Guid.NewGuid().ToString("N"));
        var incoming = Path.Combine(stagingBase, "incoming");
        var backup = Path.Combine(stagingBase, "backup");
        Directory.CreateDirectory(stagingBase);
        var backedUp = false; var installed = false;
        try
        {
            await CopyDirectoryAsync(packageDir, incoming);
            if (Directory.Exists(final)) { Directory.Move(final, backup); backedUp = true; }
            Directory.Move(incoming, final); installed = true;
            if (backedUp) TryDeleteDirectory(backup);
        }
        catch
        {
            if (!installed && backedUp && !Directory.Exists(final) && Directory.Exists(backup)) Directory.Move(backup, final);
            throw;
        }
        finally
        {
            TryDeleteDirectory(incoming); if (installed) TryDeleteDirectory(backup); TryDeleteDirectory(stagingBase);
            var stagingRoot = Path.Combine(root, ".staging");
            try { if (Directory.Exists(stagingRoot) && !Directory.EnumerateFileSystemEntries(stagingRoot).Any()) Directory.Delete(stagingRoot); } catch { }
        }
    }
    finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
}

static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

static async Task CopyDirectoryAsync(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(source, dir); Directory.CreateDirectory(Path.Combine(destination, rel));
    }
    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(source, file); var target = Path.Combine(destination, rel); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await src.CopyToAsync(dst);
    }
}

static async Task RewriteMeta(string path, bool completed, string reason, string ended)
{
    if (!File.Exists(path)) return;
    using var d = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(d.RootElement.GetRawText()) ?? new();
    map["Completed"] = completed; map["TerminationReason"] = reason; map["EndedUtc"] = ended;
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
}

static string ToCsv(ResultEventDto e, string run) => string.Join(",", new[]
{
    e.TimeUtc, F(e.TotalElapsedTimeSec), F(e.ExpElapsedTimeSec), e.Phase, F(e.PhaseElapsedTimeSec), e.EventType,
    e.Signal_Type, e.Signal_ID, e.Signal_Bearing, e.Signal_Width, e.Signal_Alpha, e.BTH_SelectedSignals,
    e.BTH_SelectedBearing, e.Class_Selected, e.Class_Types, e.Class_Correct, e.Class_Confidence,
    e.Class_Feedback, e.Alert_Type, e.Alert_Text, e.Alert_Bearing, e.Alert_SignalId, run
}.Select(Csv));
static string F(float f) => f.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
static string Csv(string? s) { s ??= ""; return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
static string Sha256Text(string text) { var b = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? "")); return Convert.ToHexString(b).ToLowerInvariant(); }
static string ReadPassword() { var b = new StringBuilder(); ConsoleKeyInfo k; while ((k = Console.ReadKey(true)).Key != ConsoleKey.Enter) { if (k.Key == ConsoleKey.Backspace && b.Length > 0) b.Length--; else if (!char.IsControl(k.KeyChar)) b.Append(k.KeyChar); } return b.ToString(); }

record PasswordRequest(string password);
record ChangePasswordRequest(string currentPassword, string newPassword);
record UploadBase64(string fileName, string base64);
record SelectedFiles(string[] files);
record FinishRequest(bool completed, string terminationReason, string endedUtc);
record EventEnvelope(long sequence, ResultEventDto @event);
record ExternalLaunchRequest(string provider, string participantId, string providerStudyId, string? sessionId, string? assignmentId, string? testToken);
record ResetStudyDataRequest(bool deleteResults);
record RunStartRequest(RunMetadata metadata, string definitionJson);
record RunExternalContext(string runId, string externalStudyId, string participantId, int sequenceIndexOneBased, bool testMode);
record ResultEventDto(string TimeUtc, float TotalElapsedTimeSec, float ExpElapsedTimeSec, float PhaseElapsedTimeSec, string Phase, string EventType, string Signal_Type, string Signal_ID, string Signal_Bearing, string Signal_Width, string Signal_Alpha, string BTH_SelectedSignals, string BTH_SelectedBearing, string Class_Selected, string Class_Types, string Class_Correct, string Class_Confidence, string Class_Feedback, string Alert_Type, string Alert_Text, string Alert_Bearing, string Alert_SignalId, string RunId);
record RunMetadata(int ResultSchemaVersion, string RunId, string SubjectId, string OperatorId, string Location, string ExperimentPackage, string ExperimentName, string ExperimentVersion, string ExperimentDefinitionHash, string ApplicationVersion, string Platform, string StartedUtc, string EndedUtc, string TerminationReason, string WaterfallPalette, long RandomizationSeed, bool Completed, string LaunchMode, string RecruitmentProvider, string ProviderParticipantId, string ProviderStudyId, string ProviderSessionId, string ProviderAssignmentId, string ExternalStudyId, string AssignmentMode, string AssignmentGroup, string[] ExperimentSequence, int SequenceIndex, int SequenceCount, bool ExternalTestMode);
record AuthData(string Salt, string Hash, string AdminKey, int MemoryKb, int Iterations, int Parallelism);

sealed class ExternalStudyGroupDto
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int weight { get; set; } = 1;
    public List<string> experiments { get; set; } = new();
    public int assignedCount { get; set; }
    public int completedCount { get; set; }
    public int testAssignedCount { get; set; }
    public int testCompletedCount { get; set; }
}

sealed class ExternalStudyConfigDto
{
    public string id { get; set; } = "";
    public string provider { get; set; } = "";
    public string providerStudyId { get; set; } = "";
    public string assignmentMode { get; set; } = "Fixed";
    public string completionUrl { get; set; } = "";
    public bool autoStart { get; set; } = true;
    public bool showInstructions { get; set; } = true;
    public bool enabled { get; set; } = true;
    public List<ExternalStudyGroupDto> groups { get; set; } = new();
    public int assignedCount { get; set; }
    public int completedCount { get; set; }
    public int testAssignedCount { get; set; }
    public int testCompletedCount { get; set; }
}

sealed class ExternalAssignmentRow
{
    public string externalStudyId { get; set; } = "";
    public string participantId { get; set; } = "";
    public string sessionId { get; set; } = "";
    public string providerAssignmentId { get; set; } = "";
    public string groupId { get; set; } = "";
    public string groupName { get; set; } = "";
    public List<string> experimentSequence { get; set; } = new();
    public int currentIndex { get; set; }
    public string status { get; set; } = "ACTIVE";
    public string assignedUtc { get; set; } = "";
    public string completedUtc { get; set; } = "";
}

sealed class ExternalLaunchReply
{
    public bool ok { get; set; }
    public string message { get; set; } = "";
    public string provider { get; set; } = "";
    public string participantId { get; set; } = "";
    public string providerStudyId { get; set; } = "";
    public string sessionId { get; set; } = "";
    public string assignmentId { get; set; } = "";
    public string externalStudyId { get; set; } = "";
    public string assignmentMode { get; set; } = "";
    public string assignmentGroup { get; set; } = "";
    public List<string> experimentSequence { get; set; } = new();
    public int sequenceIndex { get; set; }
    public int sequenceCount { get; set; }
    public string experimentPackage { get; set; } = "";
    public string completionUrl { get; set; } = "";
    public bool autoStart { get; set; }
    public bool showInstructions { get; set; }
    public bool completed { get; set; }
    public bool testMode { get; set; }
}

static class AuthStore
{
    public static async Task<AuthData> Load(string f)
    {
        if (!File.Exists(f)) throw new InvalidOperationException("Operator password has not been initialized. Run: docker compose run --rm api init-password");
        return JsonSerializer.Deserialize<AuthData>(await File.ReadAllTextAsync(f))!;
    }
    public static async Task Save(string f, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); const int mem = 65536, it = 4, par = 2;
        var hash = await Hash(password, salt, mem, it, par);
        var a = new AuthData(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), mem, it, par);
        await File.WriteAllTextAsync(f, JsonSerializer.Serialize(a, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static async Task<bool> Verify(string f, string p)
    {
        if (!File.Exists(f)) return false;
        var a = await Load(f);
        var got = await Hash(p, Convert.FromBase64String(a.Salt), a.MemoryKb, a.Iterations, a.Parallelism);
        return CryptographicOperations.FixedTimeEquals(got, Convert.FromBase64String(a.Hash));
    }
    static Task<byte[]> Hash(string p, byte[] salt, int mem, int it, int par)
    {
        var a = new Argon2id(Encoding.UTF8.GetBytes(p)) { Salt = salt, MemorySize = mem, Iterations = it, DegreeOfParallelism = par };
        return Task.FromResult(a.GetBytes(32));
    }
}

static class ExperimentFiles
{
    public sealed class Summary
    {
        public string packageName { get; set; } = "";
        public string? name { get; set; }
        public string? version { get; set; }
    }

    public static Summary[] List(string root) => Directory.GetDirectories(root).Select(d =>
    {
        var f = Path.Combine(d, "experiment.json"); if (!File.Exists(f)) return null;
        try
        {
            using var j = JsonDocument.Parse(File.ReadAllText(f)); var r = j.RootElement;
            return new Summary
            {
                packageName = Path.GetFileName(d),
                name = r.TryGetProperty("Name", out var n) ? n.GetString() : Path.GetFileName(d),
                version = r.TryGetProperty("Version", out var v) ? v.GetString() : ""
            };
        }
        catch { return null; }
    }).Where(x => x != null).Select(x => x!).ToArray();

    public static List<string> ValidatePackage(JsonElement root, string packageDir)
    {
        var e = new List<string>();
        if (root.ValueKind != JsonValueKind.Object) { e.Add("Root must be an object."); return e; }
        if (!root.TryGetProperty("SchemaVersion", out var sv) || sv.ValueKind != JsonValueKind.Number || sv.GetInt32() != 1) e.Add("SchemaVersion must be 1.");
        if (!Text(root, "Name", out _)) e.Add("Name is required.");
        if (!Text(root, "Version", out _)) e.Add("Version is required.");
        string instructions = root.TryGetProperty("InstructionsFile", out var ins) && ins.ValueKind == JsonValueKind.String ? ins.GetString()! : "instructions.md";
        CheckAsset(packageDir, instructions, "InstructionsFile", e);
        if (!root.TryGetProperty("ExperimentSettings", out var set) || set.ValueKind != JsonValueKind.Object) { e.Add("ExperimentSettings is required."); return e; }
        if (!set.TryGetProperty("RandomizationSeed", out var seed) || seed.ValueKind != JsonValueKind.Number || !seed.TryGetInt64(out var seedVal) || seedVal == 0) e.Add("RandomizationSeed must be a non-zero integer.");
        if (set.TryGetProperty("ShowResultsAtEnd", out var showResults) && (showResults.ValueKind != JsonValueKind.String || (showResults.GetString() != "Yes" && showResults.GetString() != "No"))) e.Add("ShowResultsAtEnd must be 'Yes' or 'No'.");
        if (set.TryGetProperty("HideLofarSignalsIfNoSelectedBTH", out var hideLofar) && (hideLofar.ValueKind != JsonValueKind.String || (hideLofar.GetString() != "Yes" && hideLofar.GetString() != "No"))) e.Add("HideLofarSignalsIfNoSelectedBTH must be 'Yes' or 'No'.");
        var defaultSignals = new HashSet<string>(StringComparer.Ordinal);
        if (set.TryGetProperty("SignalDefinitions", out var defs) && defs.ValueKind == JsonValueKind.Object)
        {
            foreach (var d in defs.EnumerateObject())
            {
                defaultSignals.Add(d.Name);
                if (d.Value.TryGetProperty("AudioFile", out var a) && a.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(a.GetString())) CheckAsset(packageDir, a.GetString()!, "SignalDefinitions." + d.Name + ".AudioFile", e);
            }
        }
        else e.Add("ExperimentSettings.SignalDefinitions is required.");
        if (set.TryGetProperty("BackgroundAudio", out var ba) && ba.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ba.GetString())) CheckAsset(packageDir, ba.GetString()!, "BackgroundAudio", e);
        float defaultDur = Number(set, "SignalDurationSec", 30); if (defaultDur <= 0) e.Add("SignalDurationSec must be > 0.");
        if (!set.TryGetProperty("PhaseOrder", out var po) || po.ValueKind != JsonValueKind.Array || po.GetArrayLength() == 0) { e.Add("PhaseOrder must contain at least one phase."); return e; }
        if (!root.TryGetProperty("Phases", out var phases) || phases.ValueKind != JsonValueKind.Object) { e.Add("Phases is required."); return e; }
        foreach (var pn in po.EnumerateArray())
        {
            if (pn.ValueKind != JsonValueKind.String) continue;
            string name = pn.GetString()!;
            if (!phases.TryGetProperty(name, out var phase) || phase.ValueKind != JsonValueKind.Object) { e.Add($"PhaseOrder references missing phase '{name}'."); continue; }
            if (phase.TryGetProperty("HideLofarSignalsIfNoSelectedBTH", out var phaseHideLofar) && (phaseHideLofar.ValueKind != JsonValueKind.String || (phaseHideLofar.GetString() != "Yes" && phaseHideLofar.GetString() != "No"))) e.Add($"Phase '{name}' HideLofarSignalsIfNoSelectedBTH must be 'Yes' or 'No'.");
            var available = new HashSet<string>(defaultSignals, StringComparer.Ordinal);
            if (phase.TryGetProperty("SignalDefinitions", out var ovs) && ovs.ValueKind == JsonValueKind.Array)
                foreach (var ov in ovs.EnumerateArray()) if (Text(ov, "Name", out var on))
                {
                    available.Add(on);
                    if (ov.TryGetProperty("AudioFile", out var oa) && oa.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(oa.GetString())) CheckAsset(packageDir, oa.GetString()!, $"Phase {name} signal {on} AudioFile", e);
                }
            if (phase.TryGetProperty("BackgroundAudio", out var pba) && pba.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pba.GetString())) CheckAsset(packageDir, pba.GetString()!, $"Phase {name} BackgroundAudio", e);
            float phaseDurDefault = Number(phase, "SignalDurationSec", defaultDur);
            if (!phase.TryGetProperty("Signals", out var signals) || signals.ValueKind != JsonValueKind.Array || signals.GetArrayLength() == 0) { e.Add($"Phase '{name}' must contain at least one signal."); continue; }
            float phaseEnd = 0;
            foreach (var si in signals.EnumerateArray())
            {
                if (!Text(si, "Signal", out var st)) { e.Add($"Phase '{name}' has a signal without Signal name."); continue; }
                if (!available.Contains(st)) e.Add($"Phase '{name}' references undefined signal '{st}'.");
                float appear = Number(si, "AppearSec", -1); if (appear < 0) e.Add($"Phase '{name}' signal '{st}' requires non-negative AppearSec.");
                float dur = Number(si, "DurationSec", phaseDurDefault); if (dur <= 0) e.Add($"Phase '{name}' signal '{st}' duration must be > 0.");
                phaseEnd = Math.Max(phaseEnd, appear + dur);
                float bearing = Number(si, "Bearing", -1); if (bearing < 0 || bearing > 360) e.Add($"Phase '{name}' signal '{st}' Bearing must be 0..360.");
                // Signal IDs identify a source/contact and may intentionally repeat within or across phases.
                if (si.TryGetProperty("AudioFile", out var ia) && ia.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ia.GetString())) CheckAsset(packageDir, ia.GetString()!, $"Phase {name} signal {st} AudioFile", e);
            }
            if (phase.TryGetProperty("Alerts", out var alerts) && alerts.ValueKind == JsonValueKind.Array)
                foreach (var a in alerts.EnumerateArray())
                {
                    float at = Number(a, "AppearSec", -1); if (at < 0) e.Add($"Phase '{name}' alert requires non-negative AppearSec.");
                    else if (at > phaseEnd) e.Add($"Phase '{name}' alert at {at}s occurs after computed phase end {phaseEnd}s.");
                }
        }
        return e;
    }

    static bool Text(JsonElement x, string n, out string v)
    {
        v = ""; if (!x.TryGetProperty(n, out var p) || p.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(p.GetString())) return false;
        v = p.GetString()!; return true;
    }
    static float Number(JsonElement x, string n, float d) => x.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetSingle(out var f) ? f : d;
    static void CheckAsset(string root, string rel, string label, List<string> e)
    {
        try
        {
            if (Uri.TryCreate(rel, UriKind.Absolute, out _)) { e.Add(label + " must be a package-relative path."); return; }
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) { e.Add(label + " escapes the package directory."); return; }
            if (!File.Exists(full)) e.Add(label + " references missing file '" + rel + "'.");
        }
        catch { e.Add(label + " contains an invalid path."); }
    }
}

static class Db
{
    static SqliteConnection C(string f) => new($"Data Source={f}");

    public static async Task Init(string f)
    {
        await using var c = C(f); await c.OpenAsync();
        var q = c.CreateCommand();
        q.CommandText = """
CREATE TABLE IF NOT EXISTS runs(
 run_id TEXT PRIMARY KEY, subject_id TEXT, experiment_package TEXT, started_utc TEXT,
 ended_utc TEXT, status TEXT, csv_file TEXT, meta_file TEXT, heartbeat_utc TEXT
);
CREATE TABLE IF NOT EXISTS event_sequences(run_id TEXT, seq INTEGER, PRIMARY KEY(run_id,seq));
CREATE INDEX IF NOT EXISTS ix_runs_subject_exp ON runs(subject_id,experiment_package,status);
CREATE TABLE IF NOT EXISTS external_studies(
 id TEXT PRIMARY KEY,
 provider TEXT NOT NULL,
 provider_study_id TEXT NOT NULL,
 assignment_mode TEXT NOT NULL,
 completion_url TEXT NOT NULL,
 auto_start INTEGER NOT NULL,
 show_instructions INTEGER NOT NULL,
 enabled INTEGER NOT NULL,
 groups_json TEXT NOT NULL,
 created_utc TEXT NOT NULL,
 updated_utc TEXT NOT NULL,
 UNIQUE(provider,provider_study_id)
);
CREATE TABLE IF NOT EXISTS external_assignments(
 external_study_id TEXT NOT NULL,
 participant_id TEXT NOT NULL,
 session_id TEXT NOT NULL,
 provider_assignment_id TEXT NOT NULL,
 group_id TEXT NOT NULL,
 group_name TEXT NOT NULL,
 sequence_json TEXT NOT NULL,
 current_index INTEGER NOT NULL,
 status TEXT NOT NULL,
 assigned_utc TEXT NOT NULL,
 completed_utc TEXT NOT NULL,
 PRIMARY KEY(external_study_id,participant_id)
);
CREATE INDEX IF NOT EXISTS ix_external_assignments_group ON external_assignments(external_study_id,group_id,status);
CREATE TABLE IF NOT EXISTS external_test_assignments(
 external_study_id TEXT NOT NULL,
 participant_id TEXT NOT NULL,
 session_id TEXT NOT NULL,
 provider_assignment_id TEXT NOT NULL,
 group_id TEXT NOT NULL,
 group_name TEXT NOT NULL,
 sequence_json TEXT NOT NULL,
 current_index INTEGER NOT NULL,
 status TEXT NOT NULL,
 assigned_utc TEXT NOT NULL,
 completed_utc TEXT NOT NULL,
 PRIMARY KEY(external_study_id,participant_id)
);
CREATE INDEX IF NOT EXISTS ix_external_test_assignments_group ON external_test_assignments(external_study_id,group_id,status);
CREATE TABLE IF NOT EXISTS external_test_tokens(
 token_hash TEXT PRIMARY KEY,
 external_study_id TEXT NOT NULL,
 expires_utc TEXT NOT NULL,
 created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_external_test_tokens_study ON external_test_tokens(external_study_id,expires_utc);
CREATE TABLE IF NOT EXISTS run_external_context(
 run_id TEXT PRIMARY KEY,
 external_study_id TEXT NOT NULL,
 participant_id TEXT NOT NULL,
 sequence_index_one_based INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS run_external_test_context(
 run_id TEXT PRIMARY KEY,
 external_study_id TEXT NOT NULL,
 participant_id TEXT NOT NULL,
 sequence_index_one_based INTEGER NOT NULL
);
""";
        await q.ExecuteNonQueryAsync();
    }

    public static async Task Start(string f, RunMetadata m, string csv, string meta)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "INSERT INTO runs VALUES($r,$s,$e,$st,'','ACTIVE',$c,$m,$h)";
        q.Parameters.AddWithValue("$r", m.RunId); q.Parameters.AddWithValue("$s", m.SubjectId); q.Parameters.AddWithValue("$e", m.ExperimentPackage);
        q.Parameters.AddWithValue("$st", m.StartedUtc); q.Parameters.AddWithValue("$c", csv); q.Parameters.AddWithValue("$m", meta); q.Parameters.AddWithValue("$h", DateTime.UtcNow.ToString("O"));
        await q.ExecuteNonQueryAsync();
    }

    public static async Task Heartbeat(string f, string r)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "UPDATE runs SET heartbeat_utc=$h WHERE run_id=$r AND status='ACTIVE'";
        q.Parameters.AddWithValue("$h", DateTime.UtcNow.ToString("O")); q.Parameters.AddWithValue("$r", r); await q.ExecuteNonQueryAsync();
    }

    public static async Task<(string csv, string meta)?> GetRun(string f, string r)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT csv_file,meta_file FROM runs WHERE run_id=$r"; q.Parameters.AddWithValue("$r", r);
        await using var rd = await q.ExecuteReaderAsync(); return await rd.ReadAsync() ? (rd.GetString(0), rd.GetString(1)) : null;
    }

    public static async Task<bool> HasCompleted(string f, string s, string e)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT 1 FROM runs WHERE subject_id=$s AND experiment_package=$e AND status='COMPLETED' LIMIT 1";
        q.Parameters.AddWithValue("$s", s); q.Parameters.AddWithValue("$e", e); return await q.ExecuteScalarAsync() != null;
    }

    public static async Task Finish(string f, string r, bool complete, string reason, string ended)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "UPDATE runs SET status=$s,ended_utc=$e,heartbeat_utc=$e WHERE run_id=$r";
        q.Parameters.AddWithValue("$s", complete ? "COMPLETED" : "ABORTED"); q.Parameters.AddWithValue("$e", ended); q.Parameters.AddWithValue("$r", r); await q.ExecuteNonQueryAsync();
    }

    public static async Task<bool> SequenceExists(string f, string r, long n)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT 1 FROM event_sequences WHERE run_id=$r AND seq=$n"; q.Parameters.AddWithValue("$r", r); q.Parameters.AddWithValue("$n", n); return await q.ExecuteScalarAsync() != null;
    }

    public static async Task AddSequence(string f, string r, long n)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "INSERT OR IGNORE INTO event_sequences VALUES($r,$n)"; q.Parameters.AddWithValue("$r", r); q.Parameters.AddWithValue("$n", n); await q.ExecuteNonQueryAsync();
    }

    public static async Task<object[]> Results(string f, string filter)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT csv_file,run_id,subject_id,experiment_package,started_utc,status FROM runs WHERE csv_file LIKE $f ORDER BY started_utc DESC LIMIT 5000";
        q.Parameters.AddWithValue("$f", "%" + filter + "%"); var l = new List<object>();
        await using var r = await q.ExecuteReaderAsync();
        while (await r.ReadAsync()) l.Add(new { fileName = r.GetString(0), runId = r.GetString(1), subjectId = r.GetString(2), experimentPackage = r.GetString(3), startedUtc = r.GetString(4), status = r.GetString(5) });
        return l.ToArray();
    }

    public static async Task<List<string>> MarkStale(string f, TimeSpan age)
    {
        await using var c = C(f); await c.OpenAsync(); var cutoff = DateTime.UtcNow.Subtract(age).ToString("O"); var list = new List<string>();
        var q = c.CreateCommand(); q.CommandText = "SELECT meta_file FROM runs WHERE status='ACTIVE' AND heartbeat_utc < $c"; q.Parameters.AddWithValue("$c", cutoff);
        await using (var r = await q.ExecuteReaderAsync()) while (await r.ReadAsync()) list.Add(r.GetString(0));
        var u = c.CreateCommand(); u.CommandText = "UPDATE runs SET status='ABORTED',ended_utc=$e WHERE status='ACTIVE' AND heartbeat_utc < $c";
        u.Parameters.AddWithValue("$e", DateTime.UtcNow.ToString("O")); u.Parameters.AddWithValue("$c", cutoff); await u.ExecuteNonQueryAsync();
        return list;
    }

    public static async Task<List<ExternalStudyConfigDto>> ListExternalStudies(string f)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT id,provider,provider_study_id,assignment_mode,completion_url,auto_start,show_instructions,enabled,groups_json FROM external_studies ORDER BY provider,provider_study_id";
        var result = new List<ExternalStudyConfigDto>();
        await using (var r = await q.ExecuteReaderAsync())
            while (await r.ReadAsync()) result.Add(ReadStudy(r));
        foreach (var study in result) await FillStudyCounts(c, study);
        return result;
    }

    public static async Task<ExternalStudyConfigDto?> GetExternalStudy(string f, string provider, string providerStudyId)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT id,provider,provider_study_id,assignment_mode,completion_url,auto_start,show_instructions,enabled,groups_json FROM external_studies WHERE provider=$p AND provider_study_id=$s LIMIT 1";
        q.Parameters.AddWithValue("$p", provider); q.Parameters.AddWithValue("$s", providerStudyId);
        await using var r = await q.ExecuteReaderAsync(); return await r.ReadAsync() ? ReadStudy(r) : null;
    }

    public static async Task<ExternalStudyConfigDto?> GetExternalStudyById(string f, string id)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT id,provider,provider_study_id,assignment_mode,completion_url,auto_start,show_instructions,enabled,groups_json FROM external_studies WHERE id=$id LIMIT 1";
        q.Parameters.AddWithValue("$id", id); await using var r = await q.ExecuteReaderAsync(); return await r.ReadAsync() ? ReadStudy(r) : null;
    }

    public static async Task UpsertExternalStudy(string f, ExternalStudyConfigDto study)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        var now = DateTime.UtcNow.ToString("O");
        q.CommandText = """
INSERT INTO external_studies(id,provider,provider_study_id,assignment_mode,completion_url,auto_start,show_instructions,enabled,groups_json,created_utc,updated_utc)
VALUES($id,$p,$s,$m,$c,$a,$i,$e,$g,$now,$now)
ON CONFLICT(id) DO UPDATE SET provider=$p,provider_study_id=$s,assignment_mode=$m,completion_url=$c,auto_start=$a,show_instructions=$i,enabled=$e,groups_json=$g,updated_utc=$now;
""";
        q.Parameters.AddWithValue("$id", study.id); q.Parameters.AddWithValue("$p", study.provider); q.Parameters.AddWithValue("$s", study.providerStudyId);
        q.Parameters.AddWithValue("$m", study.assignmentMode); q.Parameters.AddWithValue("$c", study.completionUrl ?? ""); q.Parameters.AddWithValue("$a", study.autoStart ? 1 : 0);
        q.Parameters.AddWithValue("$i", study.showInstructions ? 1 : 0); q.Parameters.AddWithValue("$e", study.enabled ? 1 : 0);
        q.Parameters.AddWithValue("$g", JsonSerializer.Serialize(study.groups ?? new List<ExternalStudyGroupDto>())); q.Parameters.AddWithValue("$now", now);
        await q.ExecuteNonQueryAsync();
    }

    public static async Task<int> ExternalStudyAssignmentCount(string f, string id)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        q.CommandText = "SELECT " +
            "(SELECT COUNT(*) FROM external_assignments WHERE external_study_id=$id) + " +
            "(SELECT COUNT(*) FROM external_test_assignments WHERE external_study_id=$id) + " +
            "(SELECT COUNT(*) FROM run_external_context WHERE external_study_id=$id) + " +
            "(SELECT COUNT(*) FROM run_external_test_context WHERE external_study_id=$id)";
        q.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await q.ExecuteScalarAsync());
    }

    public static async Task DeleteExternalStudy(string f, string id)
    {
        await using var c = C(f); await c.OpenAsync();
        var t = c.CreateCommand(); t.CommandText = "DELETE FROM external_test_tokens WHERE external_study_id=$id"; t.Parameters.AddWithValue("$id", id); await t.ExecuteNonQueryAsync();
        var q = c.CreateCommand(); q.CommandText = "DELETE FROM external_studies WHERE id=$id"; q.Parameters.AddWithValue("$id", id); await q.ExecuteNonQueryAsync();
    }

    public static async Task<ExternalAssignmentRow> GetOrCreateExternalAssignment(string f, ExternalStudyConfigDto study, string participantId, string sessionId, string providerAssignmentId, bool testMode = false)
    {
        await using var c = C(f); await c.OpenAsync();
        string table = testMode ? "external_test_assignments" : "external_assignments";
        await Exec(c, "BEGIN IMMEDIATE;");
        try
        {
            var existing = await GetExternalAssignment(c, study.id, participantId, testMode);
            if (existing != null)
            {
                var update = c.CreateCommand();
                update.CommandText = $"UPDATE {table} SET session_id=$s,provider_assignment_id=$a WHERE external_study_id=$id AND participant_id=$p";
                update.Parameters.AddWithValue("$s", sessionId ?? ""); update.Parameters.AddWithValue("$a", providerAssignmentId ?? ""); update.Parameters.AddWithValue("$id", study.id); update.Parameters.AddWithValue("$p", participantId);
                await update.ExecuteNonQueryAsync();
                existing.sessionId = sessionId ?? existing.sessionId; existing.providerAssignmentId = providerAssignmentId ?? existing.providerAssignmentId;
                await Exec(c, "COMMIT;");
                return existing;
            }

            var group = await ChooseGroup(c, study, testMode);
            var sequence = group.experiments ?? new List<string>();
            var row = new ExternalAssignmentRow
            {
                externalStudyId = study.id, participantId = participantId, sessionId = sessionId ?? "", providerAssignmentId = providerAssignmentId ?? "",
                groupId = group.id, groupName = group.name, experimentSequence = new List<string>(sequence), currentIndex = 0,
                status = "ACTIVE", assignedUtc = DateTime.UtcNow.ToString("O"), completedUtc = ""
            };
            var ins = c.CreateCommand();
            ins.CommandText = $"INSERT INTO {table}(external_study_id,participant_id,session_id,provider_assignment_id,group_id,group_name,sequence_json,current_index,status,assigned_utc,completed_utc) VALUES($id,$p,$s,$a,$g,$n,$seq,0,'ACTIVE',$utc,'')";
            ins.Parameters.AddWithValue("$id", row.externalStudyId); ins.Parameters.AddWithValue("$p", row.participantId); ins.Parameters.AddWithValue("$s", row.sessionId); ins.Parameters.AddWithValue("$a", row.providerAssignmentId);
            ins.Parameters.AddWithValue("$g", row.groupId); ins.Parameters.AddWithValue("$n", row.groupName); ins.Parameters.AddWithValue("$seq", JsonSerializer.Serialize(row.experimentSequence)); ins.Parameters.AddWithValue("$utc", row.assignedUtc);
            await ins.ExecuteNonQueryAsync();
            await Exec(c, "COMMIT;");
            return row;
        }
        catch
        {
            try { await Exec(c, "ROLLBACK;"); } catch { }
            throw;
        }
    }

    public static async Task<ExternalAssignmentRow?> GetExternalAssignment(string f, string studyId, string participantId, bool testMode = false)
    {
        await using var c = C(f); await c.OpenAsync(); return await GetExternalAssignment(c, studyId, participantId, testMode);
    }

    static async Task<ExternalAssignmentRow?> GetExternalAssignment(SqliteConnection c, string studyId, string participantId, bool testMode = false)
    {
        string table = testMode ? "external_test_assignments" : "external_assignments";
        var q = c.CreateCommand();
        q.CommandText = $"SELECT external_study_id,participant_id,session_id,provider_assignment_id,group_id,group_name,sequence_json,current_index,status,assigned_utc,completed_utc FROM {table} WHERE external_study_id=$id AND participant_id=$p LIMIT 1";
        q.Parameters.AddWithValue("$id", studyId); q.Parameters.AddWithValue("$p", participantId);
        await using var r = await q.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new ExternalAssignmentRow
        {
            externalStudyId = r.GetString(0), participantId = r.GetString(1), sessionId = r.GetString(2), providerAssignmentId = r.GetString(3), groupId = r.GetString(4), groupName = r.GetString(5),
            experimentSequence = JsonSerializer.Deserialize<List<string>>(r.GetString(6)) ?? new List<string>(), currentIndex = r.GetInt32(7), status = r.GetString(8), assignedUtc = r.GetString(9), completedUtc = r.GetString(10)
        };
    }

    public static async Task CompleteExternalStep(string f, string studyId, string participantId, int sequenceIndexOneBased, bool testMode = false)
    {
        await using var c = C(f); await c.OpenAsync(); string table = testMode ? "external_test_assignments" : "external_assignments"; await Exec(c, "BEGIN IMMEDIATE;");
        try
        {
            var row = await GetExternalAssignment(c, studyId, participantId, testMode);
            if (row == null) { await Exec(c, "COMMIT;"); return; }
            if (row.status == "COMPLETED" || sequenceIndexOneBased != row.currentIndex + 1) { await Exec(c, "COMMIT;"); return; }
            int next = row.currentIndex + 1;
            bool done = next >= row.experimentSequence.Count;
            var q = c.CreateCommand();
            q.CommandText = $"UPDATE {table} SET current_index=$i,status=$s,completed_utc=$u WHERE external_study_id=$id AND participant_id=$p";
            q.Parameters.AddWithValue("$i", next); q.Parameters.AddWithValue("$s", done ? "COMPLETED" : "ACTIVE"); q.Parameters.AddWithValue("$u", done ? DateTime.UtcNow.ToString("O") : ""); q.Parameters.AddWithValue("$id", studyId); q.Parameters.AddWithValue("$p", participantId);
            await q.ExecuteNonQueryAsync(); await Exec(c, "COMMIT;");
        }
        catch { try { await Exec(c, "ROLLBACK;"); } catch { } throw; }
    }

    public static async Task AddRunExternalContext(string f, RunExternalContext x)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        string table = x.testMode ? "run_external_test_context" : "run_external_context";
        q.CommandText = $"INSERT OR REPLACE INTO {table}(run_id,external_study_id,participant_id,sequence_index_one_based) VALUES($r,$s,$p,$i)"; q.Parameters.AddWithValue("$r", x.runId); q.Parameters.AddWithValue("$s", x.externalStudyId); q.Parameters.AddWithValue("$p", x.participantId); q.Parameters.AddWithValue("$i", x.sequenceIndexOneBased); await q.ExecuteNonQueryAsync();
    }

    public static async Task<RunExternalContext?> GetRunExternalContext(string f, string runId)
    {
        await using var c = C(f); await c.OpenAsync();
        foreach (var testMode in new[] { false, true })
        {
            string table = testMode ? "run_external_test_context" : "run_external_context";
            var q = c.CreateCommand(); q.CommandText = $"SELECT external_study_id,participant_id,sequence_index_one_based FROM {table} WHERE run_id=$r"; q.Parameters.AddWithValue("$r", runId);
            await using var rd = await q.ExecuteReaderAsync();
            if (await rd.ReadAsync()) return new RunExternalContext(runId, rd.GetString(0), rd.GetString(1), rd.GetInt32(2), testMode);
        }
        return null;
    }

    public static async Task<bool> RunBelongsToExternalParticipant(string f, string runId, string studyId, string participantId, bool testMode = false)
    {
        await using var c = C(f); await c.OpenAsync(); var q = c.CreateCommand();
        string table = testMode ? "run_external_test_context" : "run_external_context";
        q.CommandText = $"SELECT 1 FROM {table} WHERE run_id=$r AND external_study_id=$s AND participant_id=$p LIMIT 1";
        q.Parameters.AddWithValue("$r", runId); q.Parameters.AddWithValue("$s", studyId); q.Parameters.AddWithValue("$p", participantId); return await q.ExecuteScalarAsync() != null;
    }

    static async Task<ExternalStudyGroupDto> ChooseGroup(SqliteConnection c, ExternalStudyConfigDto study, bool testMode = false)
    {
        var groups = (study.groups ?? new List<ExternalStudyGroupDto>()).Where(g => g.experiments != null && g.experiments.Count > 0).ToList();
        if (groups.Count == 0) throw new InvalidOperationException("External study has no usable groups.");
        if (study.assignmentMode == "Fixed") return groups[0];
        if (study.assignmentMode == "Random")
        {
            int total = groups.Sum(g => Math.Max(1, g.weight)); int pick = RandomNumberGenerator.GetInt32(total);
            foreach (var g in groups) { pick -= Math.Max(1, g.weight); if (pick < 0) return g; }
            return groups[^1];
        }

        var counts = groups.ToDictionary(g => g.id, _ => 0, StringComparer.Ordinal);
        string table = testMode ? "external_test_assignments" : "external_assignments";
        var q = c.CreateCommand(); q.CommandText = $"SELECT group_id,COUNT(*) FROM {table} WHERE external_study_id=$id GROUP BY group_id"; q.Parameters.AddWithValue("$id", study.id);
        await using (var r = await q.ExecuteReaderAsync()) while (await r.ReadAsync()) if (counts.ContainsKey(r.GetString(0))) counts[r.GetString(0)] = r.GetInt32(1);
        double best = double.MaxValue; var ties = new List<ExternalStudyGroupDto>();
        foreach (var g in groups)
        {
            double score = counts[g.id] / (double)Math.Max(1, g.weight);
            if (score < best - 1e-9) { best = score; ties.Clear(); ties.Add(g); }
            else if (Math.Abs(score - best) < 1e-9) ties.Add(g);
        }
        return ties[RandomNumberGenerator.GetInt32(ties.Count)];
    }

    public static async Task<(string token, string expiresUtc)> CreateExternalTestToken(string f, string studyId, TimeSpan lifetime)
    {
        string token = Base64Url(RandomNumberGenerator.GetBytes(32));
        string hash = Sha256Token(token);
        string expires = DateTime.UtcNow.Add(lifetime).ToString("O");
        await using var c = C(f); await c.OpenAsync();
        var cleanup = c.CreateCommand(); cleanup.CommandText = "DELETE FROM external_test_tokens WHERE expires_utc < $now"; cleanup.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); await cleanup.ExecuteNonQueryAsync();
        var q = c.CreateCommand(); q.CommandText = "INSERT INTO external_test_tokens(token_hash,external_study_id,expires_utc,created_utc) VALUES($h,$s,$e,$c)";
        q.Parameters.AddWithValue("$h", hash); q.Parameters.AddWithValue("$s", studyId); q.Parameters.AddWithValue("$e", expires); q.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O")); await q.ExecuteNonQueryAsync();
        return (token, expires);
    }

    public static async Task<bool> ValidateExternalTestToken(string f, string studyId, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        await using var c = C(f); await c.OpenAsync();
        var q = c.CreateCommand(); q.CommandText = "SELECT expires_utc FROM external_test_tokens WHERE token_hash=$h AND external_study_id=$s LIMIT 1";
        q.Parameters.AddWithValue("$h", Sha256Token(token)); q.Parameters.AddWithValue("$s", studyId);
        var value = await q.ExecuteScalarAsync();
        return value is string expires && DateTime.TryParse(expires, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) && dt.ToUniversalTime() > DateTime.UtcNow;
    }

    public static async Task<(int assignmentsDeleted, int runsDeleted)> ResetExternalStudyData(string f, string resultRoot, string studyId, bool testMode, bool deleteResults)
    {
        await using var c = C(f); await c.OpenAsync();
        string assignmentTable = testMode ? "external_test_assignments" : "external_assignments";
        string contextTable = testMode ? "run_external_test_context" : "run_external_context";
        var files = new List<(string csv,string meta)>();
        var ids = new List<string>();
        if (deleteResults)
        {
            var q = c.CreateCommand();
            q.CommandText = $"SELECT r.run_id,r.csv_file,r.meta_file FROM runs r JOIN {contextTable} x ON x.run_id=r.run_id WHERE x.external_study_id=$id";
            q.Parameters.AddWithValue("$id", studyId);
            await using var rd = await q.ExecuteReaderAsync();
            while (await rd.ReadAsync()) { ids.Add(rd.GetString(0)); files.Add((rd.GetString(1), rd.GetString(2))); }
        }
        await Exec(c, "BEGIN IMMEDIATE;");
        try
        {
            if (deleteResults && ids.Count > 0)
            {
                foreach (var runId in ids)
                {
                    var ev = c.CreateCommand(); ev.CommandText = "DELETE FROM event_sequences WHERE run_id=$r"; ev.Parameters.AddWithValue("$r", runId); await ev.ExecuteNonQueryAsync();
                    var rr = c.CreateCommand(); rr.CommandText = "DELETE FROM runs WHERE run_id=$r"; rr.Parameters.AddWithValue("$r", runId); await rr.ExecuteNonQueryAsync();
                }
            }
            if (deleteResults)
            {
                var cx = c.CreateCommand(); cx.CommandText = $"DELETE FROM {contextTable} WHERE external_study_id=$id"; cx.Parameters.AddWithValue("$id", studyId); await cx.ExecuteNonQueryAsync();
            }
            var a = c.CreateCommand(); a.CommandText = $"DELETE FROM {assignmentTable} WHERE external_study_id=$id"; a.Parameters.AddWithValue("$id", studyId); int assignments = await a.ExecuteNonQueryAsync();
            await Exec(c, "COMMIT;");
            if (deleteResults)
            {
                foreach (var file in files)
                {
                    DeleteResultFamily(resultRoot, file.csv, file.meta);
                }
            }
            return (assignments, ids.Count);
        }
        catch { try { await Exec(c, "ROLLBACK;"); } catch { } throw; }
    }

    static void DeleteResultFamily(string resultRoot, string csv, string meta)
    {
        foreach (var name in new[] { csv, meta })
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var full = Path.GetFullPath(Path.Combine(resultRoot, Path.GetFileName(name)));
            if (File.Exists(full)) File.Delete(full);
        }
        if (!string.IsNullOrWhiteSpace(meta) && meta.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
        {
            var snap = Path.Combine(resultRoot, Path.GetFileName(meta[..^".meta.json".Length] + ".experiment.json"));
            if (File.Exists(snap)) File.Delete(snap);
        }
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    static string Sha256Token(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""))).ToLowerInvariant();

    static ExternalStudyConfigDto ReadStudy(SqliteDataReader r) => new()
    {
        id = r.GetString(0), provider = r.GetString(1), providerStudyId = r.GetString(2), assignmentMode = r.GetString(3), completionUrl = r.GetString(4),
        autoStart = r.GetInt32(5) != 0, showInstructions = r.GetInt32(6) != 0, enabled = r.GetInt32(7) != 0,
        groups = JsonSerializer.Deserialize<List<ExternalStudyGroupDto>>(r.GetString(8)) ?? new List<ExternalStudyGroupDto>()
    };

    static async Task FillStudyCounts(SqliteConnection c, ExternalStudyConfigDto study)
    {
        var map = study.groups.ToDictionary(g => g.id, g => g, StringComparer.Ordinal);
        foreach (var testMode in new[] { false, true })
        {
            string table = testMode ? "external_test_assignments" : "external_assignments";
            var q = c.CreateCommand(); q.CommandText = $"SELECT group_id,status,COUNT(*) FROM {table} WHERE external_study_id=$id GROUP BY group_id,status"; q.Parameters.AddWithValue("$id", study.id);
            await using var r = await q.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                string groupId = r.GetString(0), status = r.GetString(1); int count = r.GetInt32(2);
                if (testMode) { study.testAssignedCount += count; if (status == "COMPLETED") study.testCompletedCount += count; }
                else { study.assignedCount += count; if (status == "COMPLETED") study.completedCount += count; }
                if (map.TryGetValue(groupId, out var g))
                {
                    if (testMode) { g.testAssignedCount += count; if (status == "COMPLETED") g.testCompletedCount += count; }
                    else { g.assignedCount += count; if (status == "COMPLETED") g.completedCount += count; }
                }
            }
        }
    }

    static async Task Exec(SqliteConnection c, string sql)
    {
        var q = c.CreateCommand(); q.CommandText = sql; await q.ExecuteNonQueryAsync();
    }
}

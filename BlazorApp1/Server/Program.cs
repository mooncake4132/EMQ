using System.Linq;
using BlazorApp1.Server.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "EMQ Internal API", Version = "v1" });
});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddSignalR()
//     .AddJsonProtocol(options =>
// {
//     // Configure the serializer to not change the casing of property names, instead of the default "camelCase" names.
//     // Default camelCase is not supported by used Blazor.Extensions.SignalR, because it does NOT allow to specify JSON deserialization options - it just uses eg. JsonSerializer.Deserialize<TResult1>(payloads[0]) and there is no option to pass JsonSerializerOptions to System.Text.Json.JsonSerializer.Deserialize():
//     // https://github.com/BlazorExtensions/SignalR/blob/v1.0.0/src/Blazor.Extensions.SignalR/HubConnection.cs#L108 + https://github.com/BlazorExtensions/SignalR/issues/64
//     // Idea taken from: https://docs.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-3.1&tabs=dotnet#jsonmessagepack-serialization-options
//     options.PayloadSerializerOptions.PropertyNamingPolicy = null;
// })
    ;

builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});

// builder.Services.AddCors(options =>
// {
//     options.AddDefaultPolicy(policyBuilder =>
//         policyBuilder.WithOrigins("https://localhost:7021")
//             .AllowAnyMethod()
//             .AllowAnyHeader()
//             .AllowCredentials());
// });

var app = builder.Build();

// app.UseCors();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    // TODO: Address security concerns (CRIME and BREACH) https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-6.0
    // app.UseResponseCompression();
}

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.MapRazorPages();
app.MapControllers();
app.MapHub<GeneralHub>("/GeneralHub");
app.MapHub<QuizHub>("/QuizHub");
app.MapFallbackToFile("index.html");

app.Run();

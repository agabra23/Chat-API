using ChatApi.Endpoints;
using ChatApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatApi(builder.Configuration);

var app = builder.Build();

app.MapChatEndpoints();

app.Run();

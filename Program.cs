using AiVideoBot;
using AiVideoBot.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<ICodexService, CodexService>();
builder.Services.AddSingleton<IVideoPlanService, VideoPlanService>();
builder.Services.AddSingleton<IVoiceGenerationService, PiperVoiceGenerationService>();
builder.Services.AddSingleton<IVideoJobService, VideoJobService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

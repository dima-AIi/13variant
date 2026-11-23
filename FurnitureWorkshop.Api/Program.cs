using FurnitureWorkshop.Api;

var app = WorkshopHost.Build(args);
await WorkshopHost.Initialize(app);
await app.RunAsync();

public partial class Program;

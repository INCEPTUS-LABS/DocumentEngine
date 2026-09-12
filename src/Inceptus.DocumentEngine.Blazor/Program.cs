using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddInceptusBpmnModeler();
builder.Services.AddSingleton<IBpmnModelerStartupDocumentProvider>(
    BpmnDemoStartupDocumentProvider.Instance);

await builder.Build().RunAsync();

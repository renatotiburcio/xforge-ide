using Bunit;
using XForge.DesignSystem.Services;

namespace XForge.Ide.Ui.Tests;

/// <summary>
/// Base bUnit com os serviços do XForge Design System registrados.
/// </summary>
public abstract class BunitTestBase : IDisposable
{
    protected BunitContext Context { get; }

    protected BunitTestBase()
    {
        Context = new BunitContext();
        Context.Services.AddXForgeComponents();

        // XFDialog usa JS apenas para foco/escape; em teste unitário não precisa navegar.
        Context.JSInterop.SetupVoid("xfDialog.trap", _ => true);
        Context.JSInterop.SetupVoid("xfDialog.release", _ => true);
    }

    public void Dispose() => Context.Dispose();
}

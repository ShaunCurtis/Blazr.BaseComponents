# Building Blazor Base Components

## Introduction

This article describes how to build a suite of three base components for Blazor.

Before I dive into the detail, consider this simple component which displays a Bootstrap Alert.

```csharp
@if (Message is not null)
{
    <div class="alert @_alertType">
        @this.Message
    </div>
}

@code {
    [Parameter] public string? Message { get; set; }
    [Parameter] public AlertType MessageType { get; set; } = BasicAlert.AlertType.Info;

    private string _alertType => this.MessageType switch
    {
        AlertType.Success => "alert-success",
        AlertType.Warning => "alert-warning",
        AlertType.Error => "alert-danger",
        _ =>  "alert-primary"
    };

    public enum AlertType
    {
        Info,
        Success,
        Error,
        Warning,
    }
}
```

It uses little of the functionality built into `ComponentBase`.  There's no lifecycle code, no UI events or after render code.

> Consider how many times instances of components like this are loaded into memory every day, and how many times they needlessly re-rendered.  A huge number of calls to lifecycle async methods, constructing and then disposing Task state machines for no reason.  Lot's of CPU cycles and memory you (and the planet) are paying for and wasted every second.

Such components cry out for a simpler, smaller footprint base component.

I'll stick my neck out [based on my own experience] and speculate that 99% of all components are candidates for lighter weight base components.

In this article I'll decribe how to build these simpler, smaller footprint base components.  There are three.  They form a simple hierarchy: the lowest component implements the core functionality needed by all components, the higher components adds extra functionality.  The top level component is a *Black Box* replacement for `ComponentBase` with some added features.  

You can change the inheritance on `FetchData` or `Counter` or any other component you use, and you won't see any difference.

## Repository

The repository for this article is [Blazr.BaseComponents](https://github.com/ShaunCurtis/Blazr.BaseComponents).

## The Three Components

1. `BlazrUIBase` is a simple UI component with minimal functionality.
 
2. `BlazrControlBase` is a mid level control component with a single lifefcycle method and single render model. 

3. `BlazrComponentBase` is a full `ComponentBase` replacement with some additional Wrapper/Frame functionality.

## BlazrBaseComponent

All the components inherit from `BlazrBaseComponent`.  Its the base class for the base components!

It's a standard class that implements the boiler plate code used by all components.  It's abstract and doesn't implement `IComponent`.  Inheriting classes implement `IComponent`, and can either set `SetParametersAsync` as `virtual`, or fix it. 

It replicates many of the same variables and properties of `ComponentBase`.

The differences are:

1. The `Initialized` flag has changed.  It's reversed and now `protected`, so inheriting classes can access it.  It has a `NotInitialized` opposite: no need for the awkward `if(!Initialized)` conditional code. 
2. It has a Guid identifier: useful for tracking instances in debugging, and used in some of my more advanced components.
3. It has two `RenderFragments` to implement Wrapper/Frame functionality. `Frame` defines the code to wrap around `Body`. `Frame` is nullable: if it's `null` the component renders `Body` directly.

```csharp
public abstract class BlazrBaseComponent
{
    private RenderHandle _renderHandle;
    private RenderFragment _content;
    private bool _renderPending;
    private bool _hasNeverRendered = true;

    protected bool Initialized;
    protected bool NotInitialized => !this.Initialized;

    protected virtual RenderFragment? Frame { get; set; }
    protected RenderFragment Body { get; init; }

    public Guid ComponentUid { get; init; } = Guid.NewGuid();
```

The constructor implements the wrapper functionality.

1. It assigns the render code `BuildRenderTree` to `Body`.
2. It sets up the lambda method assigned to `_content` : the render fragment `StateHasChanged` passes to the Renderer.
3. The lambda method assigns `Frame` to `_content` if it's not null, otherwise it assigns `Body`.
4. The lambda method sets `Initialized` to true when it completes.

More about the frame/wrapper functionality later.

```csharp
    public BlazrBaseComponent()
    {
        this.Body = (builder) => this.BuildRenderTree(builder);

        _content = (builder) =>
        {
            _renderPending = false;
            _hasNeverRendered = false;
            if (Frame is not null)
                Frame.Invoke(builder);
            else
                BuildRenderTree(builder);

            this.Initialized = true;
        };
    }
```

The rest of the code replicates essential methods from `ComponentBase`.

`RenderAsync` is an additional method that renders the component immediately.  It works by calling `StateHasChanged` and immediately yielding by calling `await Task.Yield()`. The caller yields back to the Render and  frees the UI Synchronisation Context: the Renderer services it's queue and renders the component.

```csharp

    public void Attach(RenderHandle renderHandle)
        => _renderHandle = renderHandle;

    protected abstract void BuildRenderTree(RenderTreeBuilder builder);

    public async Task RenderAsync()
    {
        this.StateHasChanged();
        await Task.Yield();
    }

    public void StateHasChanged()
    {
        if (_renderPending)
            return;

        var shouldRender = _hasNeverRendered || this.ShouldRender() || _renderHandle.IsRenderingOnMetadataUpdate;

        if (shouldRender)
        {
            _renderPending = true;
            _renderHandle.Render(_content);
        }
    }

    protected virtual bool ShouldRender() => true;

    protected Task InvokeAsync(Action workItem)
        => _renderHandle.Dispatcher.InvokeAsync(workItem);

    protected Task InvokeAsync(Func<Task> workItem)
        => _renderHandle.Dispatcher.InvokeAsync(workItem);
```

Note: there are no lifecycle methods or implementation of `SetParametersAsync`.  It's the responsibility of the individual library classes to implement `IComponent`.  They can choose to lock `SetParametersAsync` by not making it `virtual`.

## BlazrUIBase

This is our simple implementation.

```csharp
public class BlazrUIBase : BlazrBaseComponent, IComponent
{
    public Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        this.StateHasChanged();
        return Task.CompletedTask;
    }
}
```

It inherits from `BlazrBaseComponent` and implements `IComponent`.

1. It has a fixed `SetParametersAsync`: it's can't be overridden.
2. It has no lifecycle methods.  Simple components don't need them.
3. It doesn't implement `IHandleEvent` i.e. it has no UI event handling.  If you need any, call `StateHasChanged` manually.
4. It doesn't implement `IHandleAfterRender` i.e. it has no after render handling.  If you need it, implement it manually.

## BlazrUIBase Demo

The demo implements the `BasicAlert` above, adding extra features to make it dismissible.

```csharp
@inherits BlazrUIBase

@if (Message is not null)
{
    <div class="@_css">
        @this.Message
        @if(this.IsDismissible)
        {
            <button type="button" class="btn-close" @onclick=this.Dismiss>
            </button>
        }
    </div>
}

@code {
    [Parameter] public string? Message { get; set; }
    [Parameter] public bool IsDismissible { get; set; }
    [Parameter] public EventCallback<string?> MessageChanged { get; set; }
    [Parameter] public AlertType MessageType { get; set; } = Alert.AlertType.Info;

    private string _css => new CSSBuilder("alert")
        .AddClass(_alertType)
        .AddClass(this.IsDismissible, "alert-dismissible")
        .Build();
    
        private void Dismiss()
            => MessageChanged.InvokeAsync(null);
    
    //... AlertType and _alertType code
}
```

And the demo `AlertPage`.

```csharp
@page "/AlertPage"
@inherits BlazrControlBase
<PageTitle>Index</PageTitle>

<h1>Hello, world!</h1>

Welcome to your new app.

<div class="m-2">
    <button class="btn btn-success" @onclick="() => this.SetMessageAsync(_timeString)">Set Message</button>
    <button class="btn btn-danger" @onclick="() => this.SetMessageAsync(null)">Clear Message</button>
</div>

<div class="m-3 p-2 border border-1 border-success rounded-3">
    <h5>Dismisses Correctly</h5>
    <Alert @bind-Message=@_message1 MessageType=Alert.AlertType.Success />
</div>

<div class="m-3 p-2 border border-1 border-danger rounded-3">
    <h5>Does Not Dismiss</h5>
    <Alert Message=@_message2 MessageType=Alert.AlertType.Error />
</div>

@code {
    private string? _message1;
    private string? _message2;
    private string _timeString => $"Set at {DateTime.Now.ToLongTimeString()}";

    private Task SetMessageAsync(string? message)
    {
        _message1 = message;
        _message2 = message;
        this.StateHasChanged();
        return Task.CompletedTask;
    }

}
```

There are some important points in this code to digest.

`Alert` implements the *Component Bind* pattern: A `Message` incoming getter parameter and a `MessageChanged` outgoing `EventCallback` setter parameter.   The parent can bind a variable/property to the component like this `@bind-Message=_message`.

`Alert` has a UI event, but there's no `IHandleEvent` handler implemented.  The Render still handles the event by calling the UI event method directly.  There's no built-in call to `StateAsChanged()`. 

In the Demo page there are two instances of `Alert`.  One is wired through `@bind-Message`, Two is wired through the `Message` parameter.

When you run the code and click on the buttons, Two doesn't dismiss the Alert.  The're nothing wired to `MessageChanged`.

One, on the other hand works, even though there's no call to `StateHasChanged`.

`Index` inherits from `BlazrControlBase`, so there's a built-in call to `StateHasChanged` at the end of the UI event handler.

1. The Alert `Dismiss` method invokes `MessageChanged` passing a `null` string.
2. The UI handler invokes the Bind handler in `Index`.
3. The Bind handler [created by the Razor Compiler] updates `_message` to `null`.
4. The UI Handler completes and calls `StateHasChanged`.
5. `Index` renders. 
1. The Renderer detects the `Message` parameter on `Alert` has changed.  It calls `SetParametersAsync` on `Alert` passing in the modified `ParameterView`.
7. `Alert` renders: `Message` is `null` so it hides the alert.

> The important lesson to learn is : Always test whether you actually need to call `StateHasChanged`.

### AlertPage Inheriting BlazrUIBase

We can downgrade the inheritance on `AlertPage` to `BlazrUIBase` to experiment with rendering.  

Once you do so, nothing updates.  No Alert appears because there's no `StateHasChanged()` calls happening [and no UI Render Updates] when UI events occur.

We can fix that by adding calls to `StateHasChanged` where they are needed.

Binding will no longer work as advertised: there's no longer a registered UI handler.  The renderer calls the bind handler directly.  There's no built-in call to `StateHasChanged`.

To solve this we wire up the binding manually.

1. Add a handler to assign to the `MessageChanged` callback. This calls `StateHasChanged` once it's set `_message1`.  We've replicated the original process.

```csharp
private Task OnUpdateMessage(string? value)
{
    _message1 = value;
    this.StateHasChanged();
    return Task.CompletedTask;
}
```

2. Change the binding on the `Alert` component.

```
<Alert @bind-Message:get=_message1 @bind-Message:set=this.OnUpdateMessage MessageType=Alert.AlertType.Success />
```

3. Update `SetMessageAsync` to call `StateHasChanged`.

```csharp
private Task SetMessageAsync(string? message)
{
    _message1 = message;
    _message2 = message;
    this.StateHasChanged();
    return Task.CompletedTask;
}
```

## BlazrControlBase

`BlazrControlBase` is the intermediate level component.  It's my workhorse.

It:

1. Implements the `OnParametersSetAsync` lifecycle method.
2. Implements a single render UI event handler.
3. Locks `SetParametersAsync`: you can't override it.

```csharp
public abstract class BlazrControlBase : BlazrBaseComponent, IComponent, IHandleEvent
{
    public async Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        await this.OnParametersSetAsync();
        this.StateHasChanged();
    }

    protected virtual Task OnParametersSetAsync()
        => Task.CompletedTask;  

    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? obj)
    {
        await item.InvokeAsync(obj);
        this.StateHasChanged();
    }
}
```

Consider this.

You can code `OnParametersSetAsync` to run initialization code: you now have access to the initialization state.  `OnInitialized{Async}` is redundant.

In simple scenarios you can code everything in `OnParametersSetAsync`.  In more complex scenarios, you can break out the intialization code into one or more separate methods.

```csharp
   protected override async Task OnParametersSetAsync()
    {
        if (this.NotInitialized)
        {
            // do initialization stuff here
        }
    }
```

You don't need *sync* versions.  There's no difference in overhead between:

```csharp
private Task DoParametersSet()
{
    OnParametersSet();
    return OnParametersSetAsync();
}

protected virtual void OnParametersSet()
{
    // Some sync code
}

protected virtual Task OnParametersSetAsync()
    => Task.CompletedTask;
```

And:

```csharp
protected virtual Task OnParametersSetAsync() 
{
    // some sync code
    return Task.CompletedTask;
}
```

I'd like to make it return a `ValueTask`, but that breaks compatibility. 

## BlazrControlBase Demo

The demo page looks like a normal `ComponentBase` page.  That's intentional.

### Modified Weather Forecast Data Pipeline

First the modified Weather Forecast data class and service.

```csharp
public class WeatherForecast
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public string? Summary { get; set; }
}
```

```csharp
namespace Blazr.Server.Web.Data;

public class WeatherForecastService
{
    private List<WeatherForecast> _forecasts;
    private static readonly string[] Summaries = new[]
        { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"};

    public WeatherForecastService()
        => _forecasts = this.GetForecasts();

    public async ValueTask<IEnumerable<WeatherForecast>> GetForecastsAsync()
    {
        await Task.Delay(1000);
        return _forecasts.AsEnumerable();
    }

    public async ValueTask<WeatherForecast?> GetForecastAsync(int id)
    {
        await Task.Delay(1000);
        return _forecasts.FirstOrDefault(item => item.Id == id);
    }

    private List<WeatherForecast> GetForecasts()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        return Enumerable.Range(1, 10).Select(index => new WeatherForecast
        {
            Id = index,
            Date = date.AddDays(index),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        }).ToList();
    }
}
```

### WeatherForecastViewer

This page demonstrates various features, so there's a set of buttons that use routing [rather than a button event handler that just updates the id and display] to switch between records.  They route to the same page and modify the Id - `/WeatherForecast/1`.

The markup is self-evident.  It's not efficient: it's *keep it simple* demo code.

The code I want to look at in detail is `OnParametersSetAsync`.

1. `NotInitialized` provides the conditional control: only load the WeatherForecast list on initialization.  In `ComponentBase` this code would be in `OnInitializedAsync`.
2. `hasIdChanged` detects if the Id has changed.  It's declared separately to make the code clearer and more expressive.  The compiler will optimize this. 
3. It only gets the new record if the Id has changed.

```csharp
@page "/WeatherForecast/{Id:int}"
@inject WeatherForecastService service
@inherits BlazrControlBase

<h3>Country Viewer</h3>

<div class="bg-dark text-white m-2 p-2">
    @if (_record is not null)
    {
        <pre>Id : @_record.Id </pre>
        <pre>Name : @_record.Date </pre>
        <pre>Temp C : @_record.TemperatureC </pre>
        <pre>Temp F : @_record.TemperatureF </pre>
        <pre>Summary : @_record.Summary </pre>
    }
    else
    {
        <pre>No Record Loaded</pre>
    }
</div>

<div class="m-3 text-end">
    <div class="btn-group">
        @foreach (var forecast in _forecasts)
        {
            <a class="btn @this.SelectedCss(forecast.Id)" href="@($"/WeatherForecast/{forecast.Id}")">@forecast.Id</a>
        }
    </div>
</div>
@code {
    [Parameter] public int Id { get; set; }

    private WeatherForecast? _record;
    private IEnumerable<WeatherForecast> _forecasts = Enumerable.Empty<WeatherForecast>();

    private int _id;

    private string SelectedCss(int value)
        => _id == value ? "btn-primary" : "btn-outline-primary";

    protected override async Task OnParametersSetAsync()
    {
        if (NotInitialized)
            _forecasts = await service.GetForecastsAsync();

        var hasIdChanged = this.Id != _id;

        _id = this.Id;

        if (hasIdChanged)
            _record = await service.GetForecastAsync(this.Id);
    }
}
```

### `BlazrComponentBase`

The full `ComponentBase` implementation is too long to include here: it's in the Appendix.


## BaseComponent Added Features

All the base components come with some extras.

### The Wrapper/Frame Functionality

A Demo `Wrapper` component.  

Note the wrapper is defined in the `Frame` render fragment, and uses the Razor built-in `__builder` RenderTreeBuilder instance.

```csharp
@inherits BlazrControlBase

@*Code Here is redundant*@

@code {
    protected override RenderFragment Frame => (__builder) => 
    {
        <h2 class="text-primary">Welcome To Blazor</h2>
        <div class="border border-1 border-primary rounded-3 bg-light p-2">
            @this.Body
        </div>
    };
}
```
And `Index` inheriting from `Wrapper`.

```csharp
@page "/"
@page "/WrapperDemo"

@inherits Wrapper

<PageTitle>Index</PageTitle>

<h1>Hello, world!</h1>

Welcome to your new app.

<SurveyPrompt />

```

What you get is:

![Wrapper Demo](https://github.com/ShaunCurtis/Blazr.Components/blob/master/assets/BlazrComponentBase/Wrapper-Demo.png)

### RenderAsync

When you move to the single render-on-completion or manual render UI event handling, you [the coder] get control of when you do intermediate renders.  `RenderAsync` ensures the component is rendered immediately.

The following page demonstrates how it works:

```
@page "/Load"
@inherits BlazrControlBase
<h3>SequentialLoadPage</h3>

<div class="bg-dark text-white m-2 p-2">
    <pre>@this.Log.ToString()</pre>
</div>
@code {
    private StringBuilder Log = new();

    protected override async Task OnParametersSetAsync()
    {
        await GetData();
    }

    private async Task GetData()
    {
        for(var counter = 1; counter <= 10; counter++)
        {
            this.Log.AppendLine($"Fetched Record {counter}");
            await this.RenderAsync();
            await Task.Delay(500);
        }
    }
}
```

Miss out `await this.RenderAsync();` and you only get the final result.  If you ran this code in `ComponentBase` you would get the first render, and then nothing would happen till the last.  Comment out `RenderAsync`, change the inheritance and try it. 

## Manually Implementing OnAfterRender

If you need to implement `OnAfterRender`, it's relatively simple.


```csharp
@implements IHandleAfterRender

//...  markup

@code {
    // Implement if need to detect first after render
    private bool _firstRender = true;

    Task IHandleAfterRender.OnAfterRenderAsync()
    {
        if (_firstRender)
        {
            // Do first render stuff
            _firstRender = false;
        }

        // Do subsequent render stuff
    }
}
```

## Bringing it Together

This demo page takes the `WeatherForecastViewer` and adds status information as the page loads using the `Alert` component.  Again the important code is in `OnParametersSetAsync`.

The code uses `_message`, `_alertType` and `_dismissible` class variables to control the alert box and switch the messaging.  The final completed alert is set as dismissible. 

```csharp
@page "/WeatherForecastWithStatus/{Id:int}"
@inject WeatherForecastService service
@inherits BlazrControlBase

<h3>Weather Forecast Viewer</h3>

<Alert @bind-Message=_message IsDismissible=_dismissible MessageType=_alertType/>

<div class="bg-dark text-white m-2 p-2">
    @if (_record is not null)
    {
        <pre>Id : @_record.Id </pre>
        <pre>Name : @_record.Date </pre>
        <pre>Temp C : @_record.TemperatureC </pre>
        <pre>Temp F : @_record.TemperatureF </pre>
        <pre>Summary : @_record.Summary </pre>
    }
    else
    {
        <pre>No Record Loaded</pre>
    }
</div>

<div class="m-3 text-end">
    <div class="btn-group">
        @foreach (var forecast in _forecasts)
        {
            <a class="btn @this.SelectedCss(forecast.Id)" href="@($"/WeatherForecastWithStatus/{forecast.Id}")">@forecast.Id</a>
        }
    </div>
</div>
@code {
    [Parameter] public int Id { get; set; }

    private WeatherForecast? _record;
    private IEnumerable<WeatherForecast> _forecasts = Enumerable.Empty<WeatherForecast>();
    private string? _message;
    private bool _dismissible;
    private Alert.AlertType _alertType = Alert.AlertType.Info;

    private int _id;

    private string SelectedCss(int value)
        => _id == value ? "btn-primary" : "btn-outline-primary";

    protected override async Task OnParametersSetAsync()
    {
        _dismissible = false;

        if (NotInitialized)
        {
            _message = "Initializing";
            _alertType = Alert.AlertType.Warning;
            await this.RenderAsync();
            _forecasts = await service.GetForecastsAsync();
        }

        var hasIdChanged = this.Id != _id;

        _id = this.Id;

        if (hasIdChanged)
        {
            _message = "Loading";
            _alertType = Alert.AlertType.Info;
            await this.RenderAsync();
            _record = await service.GetForecastAsync(this.Id);
        }

        _message = "Loaded";
        _alertType = Alert.AlertType.Success;
        _dismissible = true;
        await this.RenderAsync();

    }
}
```

## Summing Up

I've demonstrated that you don't need to be stuck with `ComponentBase` in your Blazor applications.  

Take the plunge. Start using my component suite.  Make `BlazrControlBase` your main base component.

I've included `BlazrComponentBase`, but I must confess to never using it.  I only use `ComponentBase` where I used components that inherit from it such as the `InputBase` edit controls.

## Appendix

### Class Diagram

![Class Diagram](https://github.com/ShaunCurtis/Blazr.Components/blob/master/assets/BlazrComponentBase/Class-Diagram.png)

### BlazrComponentBase

The full class code for `BlazrComponentBase`.

```csharp
public class BlazrComponentBase : BlazrBaseComponent, IComponent, IHandleEvent, IHandleAfterRender
{
    private bool _hasCalledOnAfterRender;

    public virtual async Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        await this.ParametersSetAsync();
    }

    protected async Task ParametersSetAsync()
    {
        Task? initTask = null;
        var hasRenderedOnYield = false;

        // If this is the initial call then we need to run the OnInitialized methods
        if (this.NotInitialized)
        {
            this.OnInitialized();
            initTask = this.OnInitializedAsync();
            hasRenderedOnYield = await this.CheckIfShouldRunStateHasChanged(initTask);
            Initialized = true;
        }

        this.OnParametersSet();
        var task = this.OnParametersSetAsync();

        // check if we need to do the render on Yield i.e.
        //  - this is not the initial run or
        //  - OnInitializedAsync did not yield
        var shouldRenderOnYield = initTask is null || !hasRenderedOnYield;

        if (shouldRenderOnYield)
            await this.CheckIfShouldRunStateHasChanged(task);
        else
            await task;

        // run the final state has changed to update the UI.
        this.StateHasChanged();
    }

    protected virtual void OnInitialized() { }

    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    protected virtual void OnParametersSet() { }

    protected virtual Task OnParametersSetAsync() => Task.CompletedTask;

    protected virtual void OnAfterRender(bool firstRender) { }

    protected virtual Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;

    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? obj)
    {
        var uiTask = item.InvokeAsync(obj);

        await this.CheckIfShouldRunStateHasChanged(uiTask);

        this.StateHasChanged();
    }

    Task IHandleAfterRender.OnAfterRenderAsync()
    {
        var firstRender = !_hasCalledOnAfterRender;
        _hasCalledOnAfterRender = true;

        OnAfterRender(firstRender);

        return OnAfterRenderAsync(firstRender);
    }

    protected async Task<bool> CheckIfShouldRunStateHasChanged(Task task)
    {
        var isCompleted = task.IsCompleted || task.IsCanceled;

        if (!isCompleted)
        {
            this.StateHasChanged();
            await task;
            return true;
        }

        return false;
    }
}
```

### CSSBuilder

And CssBuilder:

```csharp
/// ============================================================
/// Modification Author: Shaun Curtis, Cold Elm Coders
/// License: Use And Donate
/// If you use it, donate something to a charity somewhere
/// 
/// Original code based on CSSBuilder by Ed Charbeneau
/// and other implementations
/// 
/// https://github.com/EdCharbeneau/BlazorComponentUtilities/blob/master/BlazorComponentUtilities/CssBuilder.cs
/// ============================================================

public sealed class CSSBuilder
{
    private Queue<string> _cssQueue = new Queue<string>();

    public static CSSBuilder Class(string? cssFragment = null)
        => new CSSBuilder(cssFragment);

    public CSSBuilder() { }

    public CSSBuilder(string? cssFragment)
        => AddClass(cssFragment ?? String.Empty);

    public CSSBuilder AddClass(string? cssFragment)
    {
        if (!string.IsNullOrWhiteSpace(cssFragment))
            _cssQueue.Enqueue(cssFragment);
        return this;
    }

    public CSSBuilder AddClass(IEnumerable<string> cssFragments)
    {
        cssFragments.ToList().ForEach(item => _cssQueue.Enqueue(item));
        return this;
    }

    public CSSBuilder AddClass(bool WhenTrue, string cssFragment)
        => WhenTrue ? this.AddClass(cssFragment) : this;

    public CSSBuilder AddClass(bool WhenTrue, string? trueCssFragment, string? falseCssFragment)
        => WhenTrue ? this.AddClass(trueCssFragment) : this.AddClass(falseCssFragment);

    public CSSBuilder AddClassFromAttributes(IReadOnlyDictionary<string, object> additionalAttributes)
    {
        if (additionalAttributes != null && additionalAttributes.TryGetValue("class", out var val))
            _cssQueue.Enqueue(val.ToString() ?? string.Empty);
        return this;
    }

    public CSSBuilder AddClassFromAttributes(IDictionary<string, object> additionalAttributes)
    {
        if (additionalAttributes != null && additionalAttributes.TryGetValue("class", out var val))
            _cssQueue.Enqueue(val.ToString() ?? string.Empty);
        return this;
    }

    public string Build(string? CssFragment = null)
    {
        if (!string.IsNullOrWhiteSpace(CssFragment)) _cssQueue.Enqueue(CssFragment);
        if (_cssQueue.Count == 0)
            return string.Empty;
        var sb = new StringBuilder();
        foreach (var str in _cssQueue)
        {
            if (!string.IsNullOrWhiteSpace(str)) sb.Append($" {str}");
        }
        return sb.ToString().Trim();
    }
}
```
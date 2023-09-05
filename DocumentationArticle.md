# Debugging Blazor Components

Debugging components is a lot more that just putting in some break pointa and checking values.  Components operate in an async world.  By the time you check a value in the debugger, it's may well have changed.

I'll start this article with a short coding story.  This is a simple page to illustrate to debugging dilemna [and how myths can be born].

## My First Blazor Page

  I'm making a database call to get some data and want to display *Loading* while it's happening.  I'm *keeping it simple*, steering well clear of the *async* dark art.

What I code is this. 

```csharp
@page "/"

<PageTitle>The OnAfterRender Myth</PageTitle>

<h1>The OnAfterRender Myth</h1>

<div class="bg-dark text-white mt-5 m-2 p-2">
    <pre>@_state</pre>
</div>

@code {
    private string? _state = "New";

    protected override void OnInitialized()
    {
        _state = "Loading";
        TaskSync();
        _state = "Loaded";
    }

    // Emulate a synchronous blocking database operation
    private void TaskSync()
        => Thread.Sleep(1000);
}
```

And what I get is a blank screen and then *Loaded*: no intermediate *Loading*.

### StateHasChanged

I start searching, find `StateHasChanged` and update my code.

```caharp
    protected override void OnInitialized()
    {
        _state = "Loading";
        StateHasChanged();
        TaskSync();
        _state = "Loaded";
    }
```

But to no avail.  What is going on?  Is there a bug in the MS Component code?

### Task.Delay

I search further and find `await Task.Delay(1)`.  I start typing `await` and the Visual Studio editor adds an `async` to my method:

```csharp
    protected override async void OnInitialized()
```

I complete the change.

```csharp
    protected override async void OnInitialized()
    {
        _state = "Loading";
        StateHasChanged();
        await Task.Delay(1);
        TaskSync();
        _state = "Loaded";
    }
```

What I get is the opposite.  *Loading*, but no completion to `Loaded`.

### OnAfterRender

More searching reveals `OnAfterRender`.  I add it to my code.

```csharp
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            StateHasChanged();
    }
```

Great, it now works.

*problem solved*, I move on and start to use the pattern elsewhere.  

### The Not So Obvious Answer

The real answer to the problem above is obvious to a more experienced coder: use `OnInitializedAsync` and async database operations.  But to me, taking my first steps into Blazor and SPA's, my car wreck is still days or weeks down the road.  In the interim, I've learned a "dirty" anti-pattern that "works".   A myth is perpetuated: some voodoo magic acquired (that I may propogate).

## Debug.WriteLine

To debug components effecively, you need to output information realtime.  `Debug.WriteLine` and `Console.WriteLine` are your life savers.

Take the code above, add some logging as shown below.

```csharp
@page "/AsyncOnInitialized"
@using System.Diagnostics;

<PageTitle>Documented Async OnInitialized</PageTitle>

@{
    this.Log($"Render Component.");
}

<h1>The OnAfterRender Myth</h1>

<div class="bg-dark text-white mt-5 m-2 p-2">
    <pre>@_state</pre>
</div>

@code {
    private string? _state = "New";

    private string _id = Guid.NewGuid().ToString().Substring(0, 4);
    private string _type => this.GetType().Name;

    public async override Task SetParametersAsync(ParameterView parameters)
    {
        this.Log($"SetParametersAsync started.");
        await base.SetParametersAsync(parameters);
        this.Log($"SetParametersAsync completed.");
    }

    protected override async void OnInitialized()
    {
        this.Log($"OnInitialized Started.");
        _state = "Loading";
        StateHasChanged();
        await Task.Delay(1);
        TaskSync();
        this.Log($"OnInitialized Continuation.");
        _state = "Loaded";
        this.Log($"OnInitialized Completed.");
    }

    protected override Task OnInitializedAsync()
    {
        this.Log($"OnInitializedAsync.");
        return Task.CompletedTask;
    }

    protected override void OnParametersSet()
    {
        this.Log($"OnParametersSet.");
    }

    protected override Task OnParametersSetAsync()
    {
        this.Log($"OnParametersSetAsync.");
        return Task.CompletedTask;
    }

    protected override bool ShouldRender()
    {
        this.Log($"ShouldRender.");
        return true;
    }

    private void TaskSync()
        => Thread.Sleep(1000);

    private async Task TaskAsync()
        => await Task.Yield();

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            this.Log($"First OnAfterRender.");
            StateHasChanged();
        }
        else
            this.Log($"Subsequent OnAfterRender.");
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            this.Log($"First OnAfterRenderAsync.");
        }
        else
            this.Log($"Subsequent OnAfterRenderAsync.");
        return Task.CompletedTask;
    }

    private void Log(string message)
    {
        message = $"{_id} - {_type} => {message}";
        Debug.WriteLine(message);
        Console.WriteLine(message);
    }
}
```

We can now see the sequence.

```text
30af - AsyncOnInitialized => SetParametersAsync started.
30af - AsyncOnInitialized => OnInitialized Started.
// 1
30af - AsyncOnInitialized => OnInitializedAsync.
30af - AsyncOnInitialized => OnParametersSet.
30af - AsyncOnInitialized => OnParametersSetAsync.
30af - AsyncOnInitialized => SetParametersAsync completed.
30af - AsyncOnInitialized => Render Component.
// 2
30af - AsyncOnInitialized => OnInitialized Continuation.
30af - AsyncOnInitialized => OnInitialized Completed.
30af - AsyncOnInitialized => First OnAfterRender.
// 3
30af - AsyncOnInitialized => ShouldRender.
30af - AsyncOnInitialized => Render Component.
30af - AsyncOnInitialized => First OnAfterRenderAsync.
30af - AsyncOnInitialized => Subsequent OnAfterRender.
30af - AsyncOnInitialized => Subsequent OnAfterRenderAsync.
```

At  **1** things go awry. `OnInitializedAsync` and the rest of the lifecycle processes run to completion before at **2** the `OnInitialized` continuation runs and `OnInitialized` completes, including the final render.  It's become diverced from the lifecycle because `SetParametersAsync` had no Task returned to await.

At **3** `OnAfterRender` is run and calls `StateHasChanged` which renders the component, and kicks off the second `OnAfterRender` cycle.


## Docuemented ComponentBase

`DocumentedComponentBase` is a black box version of `ComponentBase` that provides full documentation of the internal processes.  It's available in the `Blazr.BaseComponents` library from Nuget.

Take our original sync code, inherit from `DocumentedComponentBase` and add a `Log` line in where we set `_state`.

```csharp
@page "/AsyncOnInitializedDocumented"
@inherits DocumentedComponentBase

<PageTitle>Documented Async OnInitialized</PageTitle>

<h1>Documented Async OnInitialized</h1>

<div class="bg-dark text-white mt-5 m-2 p-2">
    <pre>@_state</pre>
</div>

@code {
    private string? _state = "New";

    protected override void OnInitialized()
    {
        this.Log($"OnInitialized - State set to Loading.");
        _state = "Loading";
        TaskSync();
        this.Log($"OnInitialized - State set to Loaded.");
        _state = "Loaded";
    }

    private void TaskSync()
        => Thread.Sleep(1000);
}
```
This is the output.

The state is set and reset at **1** before `StateHasChanged` is called at **2** and the actual render takes place at **3**.

```text
===========================================
2c5b - AsyncOnInitializedDocumented => Component Initialized
2c5b - AsyncOnInitializedDocumented => Component Attached
2c5b - AsyncOnInitializedDocumented => SetParametersAsync Started
2c5b - AsyncOnInitializedDocumented => OnInitialized sequence Started
// 1
2c5b - AsyncOnInitializedDocumented => OnInitialized - State set to Loading.
2c5b - AsyncOnInitializedDocumented => OnInitialized - State set to Loaded.
2c5b - AsyncOnInitializedDocumented => OnInitialized sequence Completed
2c5b - AsyncOnInitializedDocumented => OnParametersSet Sequence Started
2c5b - AsyncOnInitializedDocumented => StateHasChanged Called
// 2
2c5b - AsyncOnInitializedDocumented => Render Queued
2c5b - AsyncOnInitializedDocumented => OnParametersSet Sequence Completed
2c5b - AsyncOnInitializedDocumented => SetParametersAsync Completed
// 3
2c5b - AsyncOnInitializedDocumented => Component Rendered
2c5b - AsyncOnInitializedDocumented => OnAfterRenderAsync Started
2c5b - AsyncOnInitializedDocumented => OnAfterRenderAsync Completed
```

Now move on to the fully async version:

```csharp
@page "/AsyncOnInitializedAsyncDocumented"
@inherits DocumentedComponentBase

<PageTitle>Documented Async OnInitializedAsync</PageTitle>

<h1>Documented Async OnInitializedAsync</h1>

<div class="bg-dark text-white mt-5 m-2 p-2">
    <pre>@_state</pre>
</div>

@code {
    private string? _state = "New";

    protected override async Task OnInitializedAsync()
    {
        this.Log($"OnInitialized - State set to Loading.");
        _state = "Loading";
        await TaskAsync();
        this.Log($"OnInitialized - State set to Loaded.");
        _state = "Loaded";
    }

    private async Task TaskAsync()
        => await Task.Delay(1000);
}
```

And you get this.  Note that at **1** you get a yield from the await and  between **1** and **2** a full component render cycle.  Once the async method completes you get the second full component render cycle. 


```text
===========================================
cf89 - AsyncOnInitializedAsyncDocumented => Component Initialized
cf89 - AsyncOnInitializedAsyncDocumented => Component Attached
cf89 - AsyncOnInitializedAsyncDocumented => SetParametersAsync Started
cf89 - AsyncOnInitializedAsyncDocumented => OnInitialized sequence Started
cf89 - AsyncOnInitializedAsyncDocumented => OnInitialized - State set to Loading.
// 1
cf89 - AsyncOnInitializedAsyncDocumented => Awaiting Task completion
cf89 - AsyncOnInitializedAsyncDocumented => StateHasChanged Called
cf89 - AsyncOnInitializedAsyncDocumented => Render Queued
cf89 - AsyncOnInitializedAsyncDocumented => Component Rendered
cf89 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Started
cf89 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Completed
// 2
cf89 - AsyncOnInitializedAsyncDocumented => OnInitialized - State set to Loaded.
cf89 - AsyncOnInitializedAsyncDocumented => OnInitialized sequence Completed
cf89 - AsyncOnInitializedAsyncDocumented => OnParametersSet Sequence Started
cf89 - AsyncOnInitializedAsyncDocumented => StateHasChanged Called
cf89 - AsyncOnInitializedAsyncDocumented => Render Queued
cf89 - AsyncOnInitializedAsyncDocumented => Component Rendered
cf89 - AsyncOnInitializedAsyncDocumented => OnParametersSet Sequence Completed
cf89 - AsyncOnInitializedAsyncDocumented => SetParametersAsync Completed
cf89 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Started
cf89 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Completed
```
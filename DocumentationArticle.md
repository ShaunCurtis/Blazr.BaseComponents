# Debugging Blazor Components

I'll start this article with a short story.

## My First Blazor Page

This is a simple page to illustrate how myths can be born.  You are making a database call to get some data and want to display *Loading* while it's happening.  You're *keeping it simple*, steering well clear of the *async* dark art.

What you code is this. 

What you get is a blank screen and then *Loaded*: no *Loading*.

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

### StateHasChanged

You go searching, find `StateHasChanged` and update your code.

But to no avail.

```caharp
    protected override void OnInitialized()
    {
        _state = "Loading";
        StateHasChanged();
        TaskSync();
        _state = "Loaded";
    }
```

### Task.Delay

You search further and find `await Task.Delay(1)`.  You start typing `await` and the Visual Studio editor adds an `async` to your method:

```csharp
    protected override async void OnInitialized()
```

You complete your change.

What you get is the opposite of what you had.  *Loading*, but no completion to `Loaded`.

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

### OnAfterRender

More searching reveals `OnAfterRender`.  You add it to your code.

Great, it now works as expected.

```csharp
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            StateHasChanged();
    }
```

You move on thinking *problem solved* and start to use the pattern elsewhere.

### The Obvious Answer

The real answer to the problem above is obvious to a more experienced coder: use `OnInitializedAsync` and async database operations.  But to the person new to Blazor and SPA's, that's days or weeks down the road.  Meanwhile, they've learned a "dirty" anti-pattern that "works".   A myth is perpetuated, some voodoo magic is acquired (which may get propogate), the car wreck is futher down the road.

## Debugging

Debugging components is a lot more that just putting in some break pointa and checking values.  Components operate in an async world.  By the time you check a value in the debugger, it's may well have changed.

You need to output information realtime.  `Debug.WriteLine` and `Console.WriteLine` are life savers.

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
30af - AsyncOnInitialized => OnInitializedAsync.
30af - AsyncOnInitialized => OnParametersSet.
30af - AsyncOnInitialized => OnParametersSetAsync.
30af - AsyncOnInitialized => SetParametersAsync completed.
30af - AsyncOnInitialized => Render Component.
30af - AsyncOnInitialized => OnInitialized Continuation.
30af - AsyncOnInitialized => OnInitialized Completed.
30af - AsyncOnInitialized => First OnAfterRender.
30af - AsyncOnInitialized => ShouldRender.
30af - AsyncOnInitialized => Render Component.
30af - AsyncOnInitialized => First OnAfterRenderAsync.
30af - AsyncOnInitialized => Subsequent OnAfterRender.
30af - AsyncOnInitialized => Subsequent OnAfterRenderAsync.
```





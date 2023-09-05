# Debugging Blazor Components

Debugging Blazor components is not simple.  We aren't in charge of their lifecycle and much of the activity within a component is async.  The component state at break points is often misleading.

I'll start this article with a short coding journey: someone new to Blazor building a simple data page.  It demonstrates the debugging dilemna and provides the scenario for the rest of the article.

## My First Blazor Page

I want to make a database call to get some data.  I perceive it will take a while, so I want to display *Loading* while it's happening.  I'm *keeping it simple*: steering clear of the *async* dark art.

What I code is this.  It's all synchronous, with a blocking `Thread.Sleep` to emulate a slow data store call.

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

What I get is a blank screen and then *Loaded*: no intermediate *Loading*.

*My expectation is that when I set `_state = "Loading"`, the component will [somehow] register that state change and re-render on the spot.*

I go searching.

### StateHasChanged

I find out about `StateHasChanged` and update my code. I'm now expecting the component to render immediately after I've set `_state`.

```caharp
    protected override void OnInitialized()
    {
        _state = "Loading";
        StateHasChanged();
        TaskSync();
        _state = "Loaded";
    }
```

But to no avail.  What is going on?  "There's a bug in the MS Component code" is a common conclusion.

### Task.Delay

I search further and find `await Task.Delay(1)`.  Looks asynchronous, but let's try it in my code.  I start typing `await` and the Visual Studio editor automatically adds an `async` to my method:

```csharp
    protected override async void OnInitialized()
```

I complete the change, and it compiles.  It must be OK.

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

Now what I get is the opposite.  *Loading*, but no completion to `Loaded`.

Confused and fustrated, I do more searching.

### OnAfterRender

And I find some stuff about `OnAfterRender`.  I add it to my code.

```csharp
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            StateHasChanged();
    }
```

Great.  At last.  It works.

I'm a little confused, but *problem solved*.  I've learned a pattern to code this type of problem.    I move on and start to use the pattern elsewhere.  

### What I Failed To Learn

The real answer to the problem above is obvious to more experienced coders.  You can't mix the sync and async worlds.  Use `OnInitializedAsync` and async database operations.

> Me. I'm taking my first steps down Blazor and SPA road.  My sync/async car wreck is still days or weeks down the road.  In the interim, I've learned a "dirty" anti-pattern that "works".  I may even share it!

## So, How Do I Debug Components?

### Debug.WriteLine/Console.WriteLine

To debug components effecively, you need to output information real time.  `Debug.WriteLine` and `Console.WriteLine` are your life lines.

> I call this *documenting* rather that *debugging*.  You aren't using break points, just logging what's happening and picking it apart later.

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
        => this.Log($"OnParametersSet.");

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
            this.Log($"First OnAfterRenderAsync.");

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

Run this and we can now see the sequence of events.

```text
30af - AsyncOnInitialized => SetParametersAsync started.
30af - AsyncOnInitialized => OnInitialized Started.
[3] => 30af - AsyncOnInitialized => OnInitializedAsync.
30af - AsyncOnInitialized => OnParametersSet.
30af - AsyncOnInitialized => OnParametersSetAsync.
30af - AsyncOnInitialized => SetParametersAsync completed.
30af - AsyncOnInitialized => Render Component.
[8] => 30af - AsyncOnInitialized => OnInitialized Continuation.
30af - AsyncOnInitialized => OnInitialized Completed.
[10] => 30af - AsyncOnInitialized => First OnAfterRender.
30af - AsyncOnInitialized => ShouldRender.
30af - AsyncOnInitialized => Render Component.
30af - AsyncOnInitialized => First OnAfterRenderAsync.
30af - AsyncOnInitialized => Subsequent OnAfterRender.
30af - AsyncOnInitialized => Subsequent OnAfterRenderAsync.
```

At  line 3 things start to go wrong. `OnInitializedAsync` and the rest of the lifecycle processes run to completion before at line 8 the `OnInitialized` continuation runs and `OnInitialized` completes, including the final render.  It's become diverced from the lifecycle because `SetParametersAsync` had no Task returned to await.

At line 10 `OnAfterRender` is run and calls `StateHasChanged` which renders the component, and kicks off the second `OnAfterRender` cycle.

## Documented ComponentBase

In the above example we've added a lot of manual logging code.  Do that regularly is tedious.  Also, there are things you can't log easily because you can't get to internal processes in `ComponentBase`.

This is where `DocumentedComponentBase` comes in.  It's a black box version of `ComponentBase` that provides full logging of the internal processes.

> Either copy the code from the repository for this article, or install the `Blazr.BaseComponents` Nuget package, and use the `Blazr.BaseComponents.ComponentBase` namespace.

### Documenting AsyncOnInitialized

Take our initial sync code from above.  Change the inheritance to `DocumentedComponentBase` and add a `Log` line in where we set `_state`. `Log` is a protected method provided by `DocumentedComponentBase`.

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

```text
===========================================
2c5b - AsyncOnInitializedDocumented => Component Initialized
2c5b - AsyncOnInitializedDocumented => Component Attached
2c5b - AsyncOnInitializedDocumented => SetParametersAsync Started
2c5b - AsyncOnInitializedDocumented => OnInitialized sequence Started
[5] => 2c5b - AsyncOnInitializedDocumented => OnInitialized - State set to Loading.
[6] => 2c5b - AsyncOnInitializedDocumented => OnInitialized - State set to Loaded.
2c5b - AsyncOnInitializedDocumented => OnInitialized sequence Completed
2c5b - AsyncOnInitializedDocumented => OnParametersSet Sequence Started
[9] => 2c5b - AsyncOnInitializedDocumented => StateHasChanged Called
2c5b - AsyncOnInitializedDocumented => Render Queued
2c5b - AsyncOnInitializedDocumented => OnParametersSet Sequence Completed
2c5b - AsyncOnInitializedDocumented => SetParametersAsync Completed
[13] => 2c5b - AsyncOnInitializedDocumented => Component Rendered
2c5b - AsyncOnInitializedDocumented => OnAfterRenderAsync Started
2c5b - AsyncOnInitializedDocumented => OnAfterRenderAsync Completed
```

The state is set and reset on lines 5 AND 6 before `StateHasChanged` is called on line 9 and the render takes place at line 13.  You can clearly see that `_state` is `Loaded` when the component actually renders on line 13.

### Documenting The Async Solution

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

And you get this.

```text
===========================================
cf89 - AsyncOnInitializedAsyncDocumented => Component Initialized
cf89 - AsyncOnInitializedAsyncDocumented => Component Attached
cf89 - AsyncOnInitializedAsyncDocumented => SetParametersAsync Started
cf89 - AsyncOnInitializedAsyncDocumented => OnInitialized sequence Started
cf89 - AsyncOnInitializedAsyncDocumented => OnInitialized - State set to Loading.
[6] => cf89 - AsyncOnInitializedAsyncDocumented => Awaiting Task completion
cf89 - AsyncOnInitializedAsyncDocumented => StateHasChanged Called
cf89 - AsyncOnInitializedAsyncDocumented => Render Queued
cf89 - AsyncOnInitializedAsyncDocumented => Component Rendered
cf89 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Started
[11] => cf89 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Completed
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

Note:
  
  1. At line 5 you get a yield from the await and between lines 5 and 11 a full component render cycle.  Once the async method completes you get the second full component render cycle. 
  2. The `OnInitialized{Async}/OnParametersSet{Async}` sequence executes in the correct order.

## Summing Up

What have we learned:

1. Don't mix Async and Sync Code.  The mantra is *Async All The Way*.
2. `StateHasChanged` rarely solves your problem.  It either doesn't work or masks underlying logic issues.
3. Running non JSInterop code in `OnAfterRender` may appear to solve the problem, but you inevitably need to call `StateHasChanged`.  Point 2 above then applies.  You do more renders than you need to.
4. There are no bugs in the Component code: they are in your code!  The behaviour you see is intentional.
5. Get your code logic correct and everything falls into place.
6. Don't trust break points in components to tell you the true state story.

Some important points to note:

1. `StateHasChanged` doesn't render the component.  It just places the component's `RenderFragment` on the Render Queue. The Renderer needs thread time on the `Synchronisation Context` to actually do the render.  That only happens when your code yields [through a yielding async method] or completes.

2. `OnAfterRender` is not part of the `OnInitialized{Async}/OnParametersSet{Async}` sequence.  It's an event handler that gets called once the component has rendered [just as a button click handler gets called if you click a button].

3. Component state mutation belongs in `OnInitialized{Async}/OnParametersSet{Async}`.    Don't mutate the state in `OnAfterRender{Async}`.  It's illogical: you must then call `StateHasChanged` [and do another render cycle] to reflect those changes in the UI.
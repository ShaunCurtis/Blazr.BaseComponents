# Debugging Blazor Components

Debugging Blazor components is not simple.  We don't control their lifecycle, and much of the activity within a component is async.  Examining the component state at a break point can often be misleading.

This article describes a fairly standard early Blazor coding experience and uses the code to demonstrate how to document and understand the component processes.

## Overview

I start this article with a short coding journey: someone new to Blazor building a simple data page.  It demonstrates the debugging dilemna and provides the scenario for the rest of the article.

The rest of the article walks through how to document the sequence of events within a component and introduces the `DocumentatedComponentBase` component to do automated logging.

Finally I provide some background information on some of the key processes.

## Repository and Packages

The code for this article is part of the [Blazor.BaseComponent library](https://github.com/ShaunCurtis/Blazr.BaseComponents/).

The `DocumentatedComponentBase` component is available in the `Blazr.BaseComponents` [Nuget Package](https://www.nuget.org/packages/Blazr.BaseComponents/).


## My First Blazor Page

I want to make a database call to get some data.  I perceive it will take a while, so I want to display *Loading* while it's happening.  I'm *keeping it simple*: steering clear of the *async* dark art.

What I code is this.  It's all synchronous, with a blocking `Thread.Sleep` to emulate a slow data store call.

*My expectation is that when I set `_state = "Loading"`, the component will [somehow] register that state change and re-render on the spot.*

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

I go searching.

### StateHasChanged

I find out about `StateHasChanged` and update my code. 

*I'm now expecting the component to render immediately after I've set `_state`.*

```caharp
    protected override void OnInitialized()
    {
        _state = "Loading";
        StateHasChanged();
        TaskSync();
        _state = "Loaded";
    }
```

But to no avail.  What is going on?  "Maybe I've found a bug in the MS Component code".

I do more searching.

### Task.Delay

I find `await Task.Delay(1)`.  Looks asynchronous, but let's try it in my code.  I start typing `await` and the Visual Studio editor automatically adds an `async` to my method:

```csharp
    protected override async void OnInitialized()
```

I complete the change.  It compiles so it's probably OK.

*I'm expecting it to work, but not clear why.*

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

I get is the opposite.  *Loading*, but no completion to `Loaded`.

Now confused and fustrated, I carry on searching.

### OnAfterRender

And I find some stuff about `OnAfterRender`.  I add it to my code.

```csharp
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            StateHasChanged();
    }
```

I'm hoping it will work and [sigh of relief] it does.  I don't know why [or I kid myself that I do know why]. It works, so *problem solved*.  

I've learned a new pattern to code this type of scenario.  I move on and use it elsewhere.  

### What I Failed To Learn

The real solution to the problem is obvious to more experienced coders.  You can't mix the sync and async worlds. `async void` is a deadly concoction in most situations.  Use `OnInitializedAsync` and async database operations.

> Me. I'm just taking my first steps down Blazor and SPA road.  My `async void` car wreck is still days or weeks down the road.  In the interim, I've learned a "dirty" anti-pattern that "works".  I may even share it!

## How To Debug Components

### Debug.WriteLine/Console.WriteLine

To debug components effectively, you need to output information real time.  `Debug.WriteLine` and `Console.WriteLine` are your life lines.

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

At  line 3 things start to go wrong. `OnInitializedAsync` and the rest of the lifecycle processes run to completion [including the final render], before at line 8 the `OnInitialized` continuation runs and `OnInitialized` completes.  `OnInitialized` has become detacted from the lifecycle because `SetParametersAsync` had no Task returned to await.

At line 10 `OnAfterRender` is run and calls `StateHasChanged` which renders the component, and kicks off the second `OnAfterRender` cycle.

## Documented ComponentBase

In the example I've added a lot of manual logging code.  Doing that regularly is time consuming and tedious.  Whilst most information can be logged, it's a bit clunky as there's no access to the internal `ComponentBase` processes.

This is where `DocumentedComponentBase` comes in.  It's a black box version of `ComponentBase` that provides full logging of the internal processes.

> Either copy the code from the repository for this article, or install the `Blazr.BaseComponents` Nuget package, and use the `Blazr.BaseComponents.ComponentBase` namespace.

### Documenting AsyncOnInitialized

Refactoring the initial sync code from above is easy.  Change the inheritance to `DocumentedComponentBase` and add a `Log` line in where we set `_state`. `Log` is a protected method provided by `DocumentedComponentBase`.

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

> I copy and paste the output into a text file and then annotate it.

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

The state is set and reset on lines 5 AND 6 before `StateHasChanged` is called on line 9 and the render takes place at line 13.  You can clearly see that `_state` is `Loaded` when the component actually renders on line 13.  There's no magic render between lines 5 and 6.

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
[10] => cf89 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Started
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
  
  1. At line 5 there is a yield from the `await` and between lines 5 and 11 a full component render cycle.  Once the async method completes there's the second full component render cycle. 
  2. The `OnInitialized{Async}/OnParametersSet{Async}` sequence executes in the correct order.

Make one change to the code [shortening the delay to 1ms]:

```
private async Task TaskAsync()
    => await Task.Delay(1);
```

Examine the output and note that the first `OnAfterRenderAsync` has moved from line 10 to line 18.  It's changed from executing immediately after the first render to the end of the process. 

```text
===========================================
e945 - AsyncOnInitializedAsyncDocumented => Component Initialized
e945 - AsyncOnInitializedAsyncDocumented => Component Attached
e945 - AsyncOnInitializedAsyncDocumented => SetParametersAsync Started
e945 - AsyncOnInitializedAsyncDocumented => OnInitialized sequence Started
e945 - AsyncOnInitializedAsyncDocumented => OnInitialized - State set to Loading.
e945 - AsyncOnInitializedAsyncDocumented => Awaiting Task completion
e945 - AsyncOnInitializedAsyncDocumented => StateHasChanged Called
e945 - AsyncOnInitializedAsyncDocumented => Render Queued
e945 - AsyncOnInitializedAsyncDocumented => Component Rendered
e945 - AsyncOnInitializedAsyncDocumented => OnInitialized - State set to Loaded.
e945 - AsyncOnInitializedAsyncDocumented => OnInitialized sequence Completed
e945 - AsyncOnInitializedAsyncDocumented => OnParametersSet Sequence Started
e945 - AsyncOnInitializedAsyncDocumented => StateHasChanged Called
e945 - AsyncOnInitializedAsyncDocumented => Render Queued
e945 - AsyncOnInitializedAsyncDocumented => Component Rendered
e945 - AsyncOnInitializedAsyncDocumented => OnParametersSet Sequence Completed
e945 - AsyncOnInitializedAsyncDocumented => SetParametersAsync Completed
[18] => e945 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Started
e945 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Completed
e945 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Started
e945 - AsyncOnInitializedAsyncDocumented => OnAfterRenderAsync Completed
```

The change in sequence is driven by how long it takes proceses to complete and the order they are queued on the `Synchronisation Context`.

## Summing Up

What have we learned:

1. Don't mix Async and Sync Code.  The mantra is *Async All The Way*.
2. `StateHasChanged` rarely solves your problem.  It either doesn't work or masks underlying logic issues.
3. Running non JSInterop code in `OnAfterRender` may appear to solve the problem, but you inevitably need to call `StateHasChanged`.  Point 2 above then applies.  You do more renders than you need to.
4. There are no bugs in the Component code.  The behaviour you see is intentional.
5. Get your code logic correct and everything falls into place.
6. Don't trust break points in components to tell you the true state story.

Some important points to note:

1. `StateHasChanged` doesn't render the component.  It just places the component's `RenderFragment` on the Render Queue. The Renderer needs thread time on the `Synchronisation Context` to actually do the render.  That only happens when your code yields [through a yielding async method] or completes.

2. `OnAfterRender` is not part of the `OnInitialized{Async}/OnParametersSet{Async}` sequence.  It's an event handler that gets called once the component has rendered [just as a button click handler gets called if you click a button].  Because it's triggered by a different process, there's no guarantee when it will run [as demonstrated in the two examples above].

3. Component state mutation belongs in `OnInitialized{Async}/OnParametersSet{Async}`.    Don't mutate the state in `OnAfterRender{Async}`.  It's illogical: you must then call `StateHasChanged` [and do another render cycle] to reflect those changes in the UI.

### The Synchronisation Context

A `Synchronisation Context` is a virtual thread that all UI code runs on.  It's asynchronous, but guarantees a single thread of execution i.e. there is only ever one piece of code running on the context.  You can read more about it [here](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/synchronization-context?view=aspnetcore-7.0).
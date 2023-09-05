//============================================================
//   Code from the ASPNetCore Repository
//   Original Licence:
//
//   Licensed to the .NET Foundation under one or more agreements.
//   The .NET Foundation licenses this file to you under the MIT license.

//   https://github.com/dotnet/aspnetcore/blob/main/src/Components/Components/src/ComponentBase.cs
//   Author of Modifications: Shaun Curtis, Cold Elm Coders
//   License: Use And Donate
//   If you use it, donate something to a charity somewhere
//============================================================

using Microsoft.AspNetCore.Components.Rendering;
using System.Diagnostics;

namespace Blazr.BaseComponents.ComponentBase;

public class DocumentedComponentBase : IComponent, IHandleEvent, IHandleAfterRender
{
    private RenderHandle _renderHandle;
    private RenderFragment _content;
    private bool _renderPending;
    private bool _hasNotInitialized = true;
    private bool _hasNeverRendered = true;
    private bool _hasCalledOnAfterRender;

    private string _id = Guid.NewGuid().ToString().Substring(0,4);
    private string _type;

    public DocumentedComponentBase()
    {
        this.LogBreak();
        _type = this.GetType().Name;

        _content = (builder) =>
        {
            _renderPending = false;
            _hasNeverRendered = false;
            BuildRenderTree(builder);
            this.Log("Component Rendered");
        };

        this.Log("Component Initialized");
    }

    public void Attach(RenderHandle renderHandle)
    { 
        _renderHandle = renderHandle;
        this.Log("Component Attached");
    }

    public virtual async Task SetParametersAsync(ParameterView parameters)
    {
        this.Log("SetParametersAsync Started");
        parameters.SetParameterProperties(this);
        await this.ParametersSetAsync();
        this.Log("SetParametersAsync Completed");
    }

    protected async Task ParametersSetAsync()
    {
        Task? initTask = null;
        var hasRenderedOnYield = false;

        // If this is the initial call then we need to run the OnInitialized methods
        if (_hasNotInitialized)
        {
            this.Log("OnInitialized sequence Started");
            this.OnInitialized();
            initTask = this.OnInitializedAsync();
            hasRenderedOnYield = await this.CheckIfShouldRunStateHasChanged(initTask);
            _hasNotInitialized = false;
            this.Log("OnInitialized sequence Completed");
        }

        this.Log("OnParametersSet Sequence Started");
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
        this.Log("OnParametersSet Sequence Completed");
    }

    protected virtual void OnInitialized() { }

    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    protected virtual void OnParametersSet() { }

    protected virtual Task OnParametersSetAsync() => Task.CompletedTask;

    protected virtual void OnAfterRender(bool firstRender) { }

    protected virtual Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;

    protected virtual void BuildRenderTree(RenderTreeBuilder builder) { }

    protected virtual bool ShouldRender() => true;

    public void StateHasChanged()
    {
        this.Log("StateHasChanged Called");

        if (_renderPending)
        {
            this.Log("Render Already Queued.. Aborted");
            return;
        }

        var shouldRender = _hasNeverRendered || ShouldRender() || _renderHandle.IsRenderingOnMetadataUpdate;

        if (shouldRender)
        {
            this.Log("Render Queued");
            _renderPending = true;
            _renderHandle.Render(_content);
        }
        else
            this.Log("No Render Queued - Should Render is false");
    }

    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? obj)
    {
        this.LogBreak();
        this.Log("HandleEventAsync Started");
        var uiTask = item.InvokeAsync(obj);

        await this.CheckIfShouldRunStateHasChanged(uiTask);

        this.StateHasChanged();
        this.Log("HandleEventAsync Completed");
    }

    async Task IHandleAfterRender.OnAfterRenderAsync()
    {
        this.Log("OnAfterRenderAsync Started");
        var firstRender = !_hasCalledOnAfterRender;
        _hasCalledOnAfterRender = true;

        OnAfterRender(firstRender);

        await OnAfterRenderAsync(firstRender);

        this.Log("OnAfterRenderAsync Completed");

    }

    protected async Task<bool> CheckIfShouldRunStateHasChanged(Task task)
    {
        var isCompleted = task.IsCompleted || task.IsCanceled;

        if (!isCompleted)
        {
            this.Log("Awaiting Task completion");

            this.StateHasChanged();
            await task;
            return true;
        }

        return false;
    }

    protected Task InvokeAsync(Action workItem)
        => _renderHandle.Dispatcher.InvokeAsync(workItem);

    protected Task InvokeAsync(Func<Task> workItem)
        => _renderHandle.Dispatcher.InvokeAsync(workItem);

    protected void Log(string message) 
    {
        message = $"{_id} - {_type} => {message}";
        Debug.WriteLine(message);
        Console.WriteLine(message );
    }
    protected void LogBreak()
    {
        var message = $"===========================================";
        Debug.WriteLine(message);
        Console.WriteLine(message);
    }
}
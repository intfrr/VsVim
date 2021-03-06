﻿#light

namespace Vim.Modes.Command
open Vim
open Vim.Modes
open Microsoft.VisualStudio.Text
open System.Text.RegularExpressions

type internal CommandMode
    ( 
        _buffer : IVimBuffer, 
        _processor : ICommandProcessor,
        _operations : ICommonOperations
    ) =

    let _vimData = _buffer.VimData

    // Command to show when entering command from Visual Mode
    static let FromVisualModeString = "'<,'>"

    let mutable _command = StringUtil.empty

    let mutable _bindData : BindData<RunResult> option = None

    /// Currently queued up command string
    member x.Command = _command

    /// Processing a command just entails actually updating the stored command
    member x.ProcessCommand command =
        _command <- command

    /// Run the specified command
    member x.Completed command =
        _command <- StringUtil.empty
        let result = _processor.RunCommand (command |> List.ofSeq)
        x.MaybeClearSelection false
        result

    /// User cancelled input.  Reset the selection
    member x.Cancelled () = 
        _command <- StringUtil.empty
        x.MaybeClearSelection true

    // Command mode can be validly entered with the selection active.  Consider
    // hitting ':' in Visual Mode.  The selection should be cleared when leaving
    member x.MaybeClearSelection moveCaretToStart = 
        let selection = _buffer.TextView.Selection
        if not selection.IsEmpty && not _buffer.TextView.IsClosed then 
            if moveCaretToStart then
                let point = selection.StreamSelectionSpan.SnapshotSpan.Start
                selection.Clear()
                TextViewUtil.MoveCaretToPoint _buffer.TextView point
            else 
                selection.Clear()

    member x.Process (keyInput : KeyInput) =
        let bindData = 
            match _bindData with
            | None -> 
                // First key stroke.  Create a history client and get going
                let historyClient = {
                    new IHistoryClient<int, RunResult> with
                        member this.HistoryList = _vimData.CommandHistory
                        member this.RemapMode = Some KeyRemapMode.Command
                        member this.Beep() = _operations.Beep()
                        member this.ProcessCommand _ command = x.ProcessCommand command; 0
                        member this.Completed _ command = x.Completed command
                        member this.Cancelled _ = x.Cancelled ()
                    }
                let storage = HistoryUtil.Begin historyClient 0 _command
                storage.CreateBindData()
            | Some bindData ->
                bindData
        _bindData <- None

        // Actually run the KeyInput value
        match bindData.BindFunction keyInput with
        | BindResult.Complete result ->
            match result with 
            | RunResult.Completed -> 
                ProcessResult.OfModeKind ModeKind.Normal
            | RunResult.SubstituteConfirm (span, range, data) -> 
                let switch = ModeSwitch.SwitchModeWithArgument (ModeKind.SubstituteConfirm, ModeArgument.Substitute (span, range, data))
                ProcessResult.Handled switch
        | BindResult.Cancelled ->
            ProcessResult.OfModeKind ModeKind.Normal
        | BindResult.Error ->
            ProcessResult.OfModeKind ModeKind.Normal
        | BindResult.NeedMoreInput bindData ->
            _bindData <- Some bindData
            ProcessResult.Handled ModeSwitch.NoSwitch

    interface ICommandMode with
        member x.VimTextBuffer = _buffer.VimTextBuffer
        member x.Command = _command
        member x.CommandNames = Seq.empty
        member x.ModeKind = ModeKind.Command
        member x.CanProcess ki = true
        member x.Process keyInput = x.Process keyInput
        member x.OnEnter arg =
            _command <- 
                match arg with
                | ModeArgument.None -> StringUtil.empty
                | ModeArgument.OneTimeCommand _ -> StringUtil.empty
                | ModeArgument.FromVisual -> FromVisualModeString
                | ModeArgument.Substitute _ -> StringUtil.empty
                | ModeArgument.InitialVisualSelection _ -> StringUtil.empty
                | ModeArgument.InsertWithCount _ -> StringUtil.empty
                | ModeArgument.InsertWithCountAndNewLine _ -> StringUtil.empty
                | ModeArgument.InsertWithTransaction transaction -> transaction.Complete(); StringUtil.empty
        member x.OnLeave () = 
            x.MaybeClearSelection true
            _command <- StringUtil.empty
        member x.OnClose() = ()

        member x.RunCommand command = 
            _processor.RunCommand (command |> List.ofSeq)



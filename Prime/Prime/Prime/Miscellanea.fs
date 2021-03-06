﻿// Prime - A PRIMitivEs code library.
// Copyright (C) Bryan Edds, 2012-2014.

namespace Prime
open System
open System.Diagnostics
open System.ComponentModel
open System.Reflection

[<AutoOpen>]
module Miscellanea =

    /// The tautology function.
    /// No matter what you pass it, it returns true.
    let inline tautology _ = true

    /// The absurdity function.
    /// No matter what you pass it, it returns false.
    let inline absurdity _ = false

    /// Convert any value to an obj.
    let inline objectify x = x :> obj

    /// A generic identification code type.
    type Id = int64

    /// The invalid Id.
    let [<Literal>] InvalidId = 0L

    /// Perform a formatted ToString operation on a formattable object.
    let inline stringf (formattable : IFormattable) format =
        formattable.ToString (format, null)

    /// Perform a formatted ToString operation on a formattable object.
    let inline stringfp (formattable : IFormattable) format formatProvider =
        formattable.ToString (format, formatProvider)

    /// Apply a function recursively a number of times.
    let rec doTimes fn arg times =
        if times < 0 then failwith "Cannot call doTimes with times < 0."
        elif times = 0 then arg
        else doTimes fn (fn arg) (times - 1)

    /// Perform an operation until a predicate passes.
    let rec doUntil op pred =
        if not <| pred () then
            op ()
            doUntil op pred

    /// Make a function that gets a unique number.
    /// TODO: place a mutex lock in this
    /// TODO: see if returned function can be optimized by minimizing dereferences
    let makeIdMaker () =
        let id = ref InvalidId
        let makeId =
            (fun () ->
                id := !id + 1L
                if !id = 0L then Debug.Fail "Id counter overflowed (flipped back to zero). Big trouble likely ahead!"
                !id)
        makeId

    /// Add a custom TypeConverter to an existing type.
    let assignTypeConverter<'t, 'c> () =
        ignore <| TypeDescriptor.AddAttributes (typeof<'t>, TypeConverterAttribute typeof<'c>)

    /// Short-hand for linq enumerable cast.
    let enumerable =
        System.Linq.Enumerable.Cast

    /// Convert a couple of ints to a Guid value.
    /// It is the user's responsibility to ensure uniqueness when using the resulting Guids.
    let intsToGuid m n =
        let bytes = Array.create<byte> 8 (byte 0)
        Guid (m, int16 (n >>> 16), int16 n, bytes)

    /// Combine the contents of two maps, taking an item from the second map in the case of a key
    /// conflict.
    let inline (^^) map map2 =
        Map.fold
            (fun map key value -> Map.add key value map)
            map
            map2

    /// Along with the Symbol binding, is used to elaborate the name of a symbol without using a
    /// string literal.
    type SymbolName =
        { DummyField : unit }
        static member (?) (_, name) = name

    /// Along with the SymbolName type, is used to elaborate the name of a field without using a
    /// string literal.
    ///
    /// Usage:
    ///     let fieldName = Symbol?MySymbolName
    let Symbol = { DummyField = () }
    
    /// Symbol alias for module names.
    /// Needed since we can't utter something like typeof<MyModule>.
    let Module = Symbol
﻿namespace Nu
open System
open System.IO
open System.Xml
open System.Reflection
open Prime
open Nu
open Nu.Constants

[<AutoOpen>]
module GroupModule =

    type Group with

        static member register (address : Address) (group : Group) (world : World) : Group * World =
            group.DispatcherNp.Register (address, group, world)
        
        static member unregister (address : Address) (group : Group) (world : World) : Group * World =
            group.DispatcherNp.Unregister (address, group, world)

        static member make dispatcher optName =
            let id = Core.makeId ()
            { Group.Id = id
              Name = match optName with None -> string id | Some name -> name
              DispatcherNp = dispatcher
              Xtension = { XFields = Map.empty; CanDefault = false; Sealed = true }}

[<AutoOpen>]
module WorldGroupModule =

    type World with

        static member private optGroupFinder address world =
            match address.AddrList with
            | [screenName; groupName] ->
                let optGroupMap = Map.tryFind screenName world.Groups
                match optGroupMap with
                | Some groupMap -> Map.tryFind groupName groupMap
                | None -> None
            | _ -> failwith <| "Invalid group address '" + string address + "'."

        static member private groupAdder address world child =
            match address.AddrList with
            | [screenName; groupName] ->
                let optGroupMap = Map.tryFind screenName world.Groups
                match optGroupMap with
                | Some groupMap ->
                    let groupMap = Map.add groupName child groupMap
                    { world with Groups = Map.add screenName groupMap world.Groups }
                | None -> { world with Groups = Map.singleton screenName <| Map.singleton groupName child }
            | _ -> failwith <| "Invalid group address '" + string address + "'."

        static member private groupRemover address world =
            match address.AddrList with
            | [screenName; groupName] ->
                let optGroupMap = Map.tryFind screenName world.Groups
                match optGroupMap with
                | Some groupMap ->
                    let groupMap = Map.remove groupName groupMap
                    { world with Groups = Map.add screenName groupMap world.Groups }
                | None -> world
            | _ -> failwith <| "Invalid group address '" + string address + "'."

        static member getGroup address world = Option.get <| World.optGroupFinder address world
        static member setGroup address group world = World.groupAdder address world group
        static member getOptGroup address world = World.optGroupFinder address world
        static member containsGroup address world = Option.isSome <| World.getOptGroup address world
        static member private setOptGroup address optGroup world =
            match optGroup with
            | Some group -> World.setGroup address group world
            | None -> World.groupRemover address world

        static member getGroups1 world =
            seq {
                for screenKvp in world.Entities do
                    for groupKvp in screenKvp.Value do
                        let address = Address.make [screenKvp.Key; groupKvp.Key]
                        yield (address, groupKvp.Value) }

        static member getGroups screenAddress world =
            match screenAddress.AddrList with
            | [screenName] ->
                match Map.tryFind screenName world.Groups with
                | Some groupMap -> groupMap
                | None -> Map.empty
            | _ -> failwith <| "Invalid screen address '" + string screenAddress + "'."

        static member getGroups3 screenAddress groupNames world =
            let groupNames = Set.ofSeq groupNames
            let groups = World.getGroups screenAddress world
            Map.filter (fun groupName _ -> Set.contains groupName groupNames) groups

        static member registerGroup address group world =
            Group.register address group world

        static member unregisterGroup address group world =
            Group.unregister address group world

        static member removeGroupImmediate address group world =
            let world = World.publish4 (RemovingEventName + address) address (NoData ()) world
            let (group, world) = World.unregisterGroup address group world
            let entities = World.getEntities address world
            let world = snd <| World.removeEntitiesImmediate address entities world
            let world = World.setOptGroup address None world
            (group, world)

        static member removeGroup address (group : Group) world =
            let task =
                { ScheduledTime = world.State.TickTime
                  Operation = fun world ->
                    match World.getOptGroup address world with
                    | Some group -> snd <| World.removeGroupImmediate address group world
                    | None -> world }
            let world = World.addTask task world
            (group, world)

        static member removeGroupsImmediate screenAddress groups world =
            World.transformSimulants World.removeGroupImmediate screenAddress groups world

        static member removeGroups screenAddress groups world =
            World.transformSimulants World.removeGroup screenAddress groups world

        static member addGroup address group entities world =
            let (group, world) =
                match World.getOptGroup address world with
                | Some _ -> World.removeGroupImmediate address group world
                | None -> (group, world)
            let world = World.setGroup address group world
            let world = snd <| World.addEntities address entities world
            let (group, world) = World.registerGroup address group world
            let world = World.publish4 (AddEventName + address) address (NoData ()) world
            (group, world)

        static member addGroups screenAddress groupDescriptors world =
            List.fold
                (fun (groups, world) (groupName, group, entities) ->
                    let (group, world) = World.addGroup (screenAddress @+ [groupName]) group entities world
                    (group :: groups, world))
                ([], world)
                groupDescriptors
    
        static member writeGroup overlayer (writer : XmlWriter) (group : Group) entities =
            writer.WriteStartElement typeof<Group>.Name
            writer.WriteAttributeString (DispatcherNameAttributeName, (group.DispatcherNp.GetType ()).Name)
            Serialization.writePropertiesFromTarget tautology writer group
            World.writeEntities overlayer writer entities
    
        static member readGroup (groupNode : XmlNode) defaultDispatcherName defaultEntityDispatcherName world =

            // read in the dispatcher name and create the dispatcher
            let dispatcherName = Serialization.readDispatcherName defaultDispatcherName groupNode
            let dispatcher =
                match Map.tryFind dispatcherName world.Components.GroupDispatchers with
                | Some dispatcher -> dispatcher
                | None ->
                    note <| "Could not locate dispatcher '" + dispatcherName + "'."
                    let dispatcherName = typeof<GroupDispatcher>.Name
                    Map.find dispatcherName world.Components.GroupDispatchers
            
            // make the bare group with name as id
            let group = Group.make dispatcher None
            
            // attach the group's instrinsic fields from its dispatcher if any
            Reflection.attachFields group.DispatcherNp group

            // read the groups's properties
            Serialization.readPropertiesToTarget groupNode group
            
            // read the group's entities
            let entities = World.readEntities (groupNode : XmlNode) defaultEntityDispatcherName world

            // return the initialized group and entities
            (group, entities)

        static member makeGroup dispatcherName optName world =
            let dispatcher = Map.find dispatcherName world.Components.GroupDispatchers
            let group = Group.make dispatcher optName
            Reflection.attachFields dispatcher group
            group
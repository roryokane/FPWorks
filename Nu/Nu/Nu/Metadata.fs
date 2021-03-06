﻿namespace Nu
open System
open System.ComponentModel
open System.Collections.Generic
open System.Drawing
open System.IO
open System.Linq
open System.Xml
open OpenTK
open TiledSharp
open Prime
open Nu
open Nu.Constants

[<AutoOpen>]
module MetadataModule =

    /// Metadata for an asset. Useful to describe various attributes of an asset without having the
    /// full asset loaded into memory.
    type [<StructuralEquality; NoComparison>] AssetMetadata =
        | TextureMetadata of Vector2I
        | TileMapMetadata of string * Image list * TmxMap
        | SoundMetadata
        | SongMetadata
        | OtherMetadata of obj
        | InvalidMetadata of string

    /// A map of asset names to asset metadata.
    type AssetMetadataMap = Map<string, Map<string, AssetMetadata>>

    /// Thrown when a tile set property is not found.
    exception TileSetPropertyNotFoundException of string

[<RequireQualifiedAccess>]
module Metadata =

    let private getTileSetProperties (tileSet : TmxTileset) =
        let properties = tileSet.Properties
        try { ImageAssetName = properties.["ImageAssetName"]
              PackageName = properties.["PackageName"] }
        with :? KeyNotFoundException ->
            let errorMessage = "TileSet '" + tileSet.Name + "' missing one or more properties (ImageAssetName or PackageName)."
            raise <| TileSetPropertyNotFoundException errorMessage

    let private generateTextureMetadata asset =
        if not <| File.Exists asset.FileName then
            let errorMessage = "Failed to load Bitmap due to missing file '" + asset.FileName + "'."
            trace errorMessage
            InvalidMetadata errorMessage
        else
            try use bitmap = new Bitmap (asset.FileName) in TextureMetadata <| Vector2I (bitmap.Width, bitmap.Height)
            with _ as exn ->
                let errorMessage = "Failed to load Bitmap '" + asset.FileName + "' due to '" + string exn + "'."
                trace errorMessage
                InvalidMetadata errorMessage

    let private generateTileMapMetadata asset =
        try let tmxMap = TmxMap asset.FileName
            let tileSets = List.ofSeq tmxMap.Tilesets
            let tileSetImages = List.map getTileSetProperties tileSets
            TileMapMetadata (asset.FileName, tileSetImages, tmxMap)
        with _ as exn ->
            let errorMessage = "Failed to load TmxMap '" + asset.FileName + "' due to '" + string exn + "'."
            trace errorMessage
            InvalidMetadata errorMessage

    let private generateAssetMetadata asset =
        let extension = Path.GetExtension asset.FileName
        let metadata =
            match extension with
            | ".bmp"
            | ".png" -> generateTextureMetadata asset
            | ".tmx" -> generateTileMapMetadata asset
            | ".wav" -> SoundMetadata
            | ".ogg" -> SongMetadata
            | _ -> InvalidMetadata <| "Could not load asset metadata '" + string asset + "' due to unknown extension '" + extension + "'."
        (asset.Name, metadata)

    let private generateAssetMetadataSubmap (packageNode : XmlNode) =
        let packageName = (packageNode.Attributes.GetNamedItem NameAttributeName).InnerText
        let optAssets =
            List.map
                (fun assetNode -> Assets.tryLoadAssetFromAssetNode assetNode)
                (List.ofSeq <| packageNode.OfType<XmlNode> ())
        let assets = List.definitize optAssets
        debugIf
            (fun () -> assets.Count () <> optAssets.Count ())
            ("Invalid asset node in '" + packageName + "'.")
        let submap = Map.ofListBy (fun asset -> generateAssetMetadata asset) assets
        (packageName, submap)

    let private generateAssetMetadataMap packageNodes =
        List.fold
            (fun assetMetadataMap (packageNode : XmlNode) ->
                let (packageName, submap) = generateAssetMetadataSubmap packageNode
                Map.add packageName submap assetMetadataMap)
            Map.empty
            packageNodes

    /// Attempt to generate an asset metadata map from the given asset graph.
    let tryGenerateAssetMetadataMap assetGraphFileName =
        try let document = XmlDocument ()
            document.Load (assetGraphFileName : string) 
            let optRootNode = document.[RootNodeName]
            match optRootNode with
            | null -> Left <| "Root node is missing from asset graph file '" + assetGraphFileName + "'."
            | rootNode ->
                let possiblePackageNodes = List.ofSeq <| rootNode.OfType<XmlNode> ()
                let packageNodes = List.filter (fun (node : XmlNode) -> node.Name = PackageNodeName) possiblePackageNodes
                let assetMetadataMap = generateAssetMetadataMap packageNodes
                Right assetMetadataMap
        with exn -> Left <| string exn

    /// Try to get the metadata of the given asset.
    let tryGetMetadata assetName packageName assetMetadataMap =
        let optPackage = Map.tryFind packageName assetMetadataMap
        match optPackage with
        | Some package ->
            let optAsset = Map.tryFind assetName package
            match optAsset with
            | Some _ as asset -> asset
            | None -> None
        | None -> None

    /// Try to get the texture metadata of the given asset.
    let tryGetTextureMetadata assetName packageName assetMetadataMap =
        let optAsset = tryGetMetadata assetName packageName assetMetadataMap
        match optAsset with
        | Some (TextureMetadata size) -> Some size
        | None -> None
        | _ -> None

    /// Try to get the texture size metadata of the given asset.
    let tryGetTextureSizeAsVector2 assetName packageName assetMetadataMap =
        let optMetadata = tryGetTextureMetadata assetName packageName assetMetadataMap
        match optMetadata with
        | Some metadata -> Some <| Vector2 (single metadata.X, single metadata.Y)
        | None -> None

    /// Forcibly get the texture size metadata of the given asset (throwing on failure).
    let getTextureSizeAsVector2 assetName packageName assetMetadataMap =
        Option.get <| tryGetTextureSizeAsVector2 assetName packageName assetMetadataMap

    /// Try to get the tile map metadata of the given asset.
    let tryGetTileMapMetadata assetName packageName assetMetadataMap =
        let optAsset = tryGetMetadata assetName packageName assetMetadataMap
        match optAsset with
        | Some (TileMapMetadata (fileName, images, tmxMap)) -> Some (fileName, images, tmxMap)
        | None -> None
        | _ -> None

    /// Forcibly get the tile map metadata of the given asset (throwing on failure).
    let getTileMapMetadata assetName packageName assetMetadataMap =
        Option.get <| tryGetTileMapMetadata assetName packageName assetMetadataMap
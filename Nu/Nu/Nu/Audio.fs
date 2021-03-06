﻿namespace Nu
open System
open System.IO
open System.ComponentModel
open SDL2
open Prime
open Nu
open Nu.Constants

[<AutoOpen>]
module AudioModule =

    /// Describes a song asset.
    type [<StructuralEquality; NoComparison; XDefaultValue (DefaultSongValue)>] Song =
        { SongAssetName : string
          PackageName : string }

    /// Describes a sound asset.
    type [<StructuralEquality; NoComparison; XDefaultValue (DefaultSoundValue)>] Sound =
        { SoundAssetName : string
          PackageName : string }

    /// A message to the audio system to play a song.
    type [<StructuralEquality; NoComparison>] PlaySongMessage =
        { Song : Song
          Volume : single
          TimeToFadeOutSongMs : int }

    /// A message to the audio system to play a sound.
    type [<StructuralEquality; NoComparison>] PlaySoundMessage =
        { Volume : single
          Sound : Sound }
          
    /// Hint that an audio asset package with the given name should be loaded. Should be used to
    /// avoid loading assets at inconvenient times (such as in the middle of game play!)
    type [<StructuralEquality; NoComparison>] HintAudioPackageUseMessage =
        { PackageName : string }

    /// Hint that an audio package should be unloaded since its assets will not be used again (or
    /// until specified via a HintAudioPackageUseMessage).
    type [<StructuralEquality; NoComparison>] HintAudioPackageDisuseMessage =
        { PackageName : string }

    /// A message to the audio system.
    type [<StructuralEquality; NoComparison>] AudioMessage =
        | HintAudioPackageUseMessage of HintAudioPackageUseMessage
        | HintAudioPackageDisuseMessage of HintAudioPackageDisuseMessage
        | PlaySoundMessage of PlaySoundMessage
        | PlaySongMessage of PlaySongMessage
        | FadeOutSongMessage of int
        | StopSongMessage
        | ReloadAudioAssetsMessage

    /// An audio asset used by the audio system.
    type [<ReferenceEquality>] AudioAsset =
        | WavAsset of nativeint
        | OggAsset of nativeint

    /// The audio player. Represents the audio system of Nu generally.
    type [<ReferenceEquality>] AudioPlayer =
        { AudioContext : unit // audio context, interestingly, is global. Good luck encapsulating that!
          AudioAssetMap : AudioAsset AssetMap
          OptCurrentSong : PlaySongMessage option
          OptNextPlaySong : PlaySongMessage option
          AssetGraphFileName : string }

    /// Converts Sound types.
    type SoundTypeConverter () =
        inherit TypeConverter ()
        override this.CanConvertTo (_, destType) =
            destType = typeof<string>
        override this.ConvertTo (_, culture, source, _) =
            let s = source :?> Sound
            String.Format (culture, "{0};{1}", s.SoundAssetName, s.PackageName) :> obj
        override this.CanConvertFrom (_, sourceType) =
            sourceType = typeof<Sound> || sourceType = typeof<string>
        override this.ConvertFrom (_, _, source) =
            let sourceType = source.GetType ()
            if sourceType = typeof<Sound> then source
            else
                let args = (source :?> string).Split ';'
                { SoundAssetName = args.[0]; PackageName = args.[1] } :> obj

    /// Converts Song types.
    type SongTypeConverter () =
        inherit TypeConverter ()
        override this.CanConvertTo (_, destType) =
            destType = typeof<string>
        override this.ConvertTo (_, culture, source, _) =
            let s = source :?> Song
            String.Format (culture, "{0};{1}", s.SongAssetName, s.PackageName) :> obj
        override this.CanConvertFrom (_, sourceType) =
            sourceType = typeof<Song> || sourceType = typeof<string>
        override this.ConvertFrom (_, _, source) =
            let sourceType = source.GetType ()
            if sourceType = typeof<Song> then source
            else
                let args = (source :?> string).Split ';'
                { SongAssetName = args.[0]; PackageName = args.[1] } :> obj

[<RequireQualifiedAccess>]
module Audio = 

    /// Initializes the type converters found in AudioModule.
    let initTypeConverters () =
        assignTypeConverter<Sound, SoundTypeConverter> ()
        assignTypeConverter<Song, SongTypeConverter> ()

    let private haltSound () =
        ignore <| SDL_mixer.Mix_HaltMusic ()
        let channelCount = ref 0
        ignore <| SDL_mixer.Mix_QuerySpec (ref 0, ref 0us, channelCount)
        for i in [0 .. !channelCount - 1] do
            ignore <| SDL_mixer.Mix_HaltChannel i

    let private tryLoadAudioAsset2 (asset : Asset) =
        let extension = Path.GetExtension asset.FileName
        match extension with
        | ".wav" ->
            let optWav = SDL_mixer.Mix_LoadWAV asset.FileName
            if optWav <> IntPtr.Zero then Some (asset.Name, WavAsset optWav)
            else
                let errorMsg = SDL.SDL_GetError ()
                trace <| "Could not load wav '" + asset.FileName + "' due to '" + errorMsg + "'."
                None
        | ".ogg" ->
            let optOgg = SDL_mixer.Mix_LoadMUS asset.FileName
            if optOgg <> IntPtr.Zero then Some (asset.Name, OggAsset optOgg)
            else
                let errorMsg = SDL.SDL_GetError ()
                trace <| "Could not load ogg '" + asset.FileName + "' due to '" + errorMsg + "'."
                None
        | _ -> trace <| "Could not load audio asset '" + string asset + "' due to unknown extension '" + extension + "'."; None

    let private tryLoadAudioPackage packageName audioPlayer =
        let optAssets = Assets.tryLoadAssetsFromPackage (Some AudioAssociation) packageName audioPlayer.AssetGraphFileName
        match optAssets with
        | Right assets ->
            let optAudioAssets = List.map tryLoadAudioAsset2 assets
            let audioAssets = List.definitize optAudioAssets
            let optAudioAssetMap = Map.tryFind packageName audioPlayer.AudioAssetMap
            match optAudioAssetMap with
            | Some audioAssetMap ->
                let audioAssetMap = Map.addMany audioAssets audioAssetMap
                { audioPlayer with AudioAssetMap = Map.add packageName audioAssetMap audioPlayer.AudioAssetMap }
            | None ->
                let audioAssetMap = Map.ofSeq audioAssets
                { audioPlayer with AudioAssetMap = Map.add packageName audioAssetMap audioPlayer.AudioAssetMap }
        | Left error ->
            trace <| "HintAudioPackageUseMessage failed due unloadable assets '" + error + "' for '" + string (packageName, audioPlayer.AssetGraphFileName) + "'."
            audioPlayer
        
    let private tryLoadAudioAsset packageName assetName audioPlayer =
        let optAssetMap = Map.tryFind packageName audioPlayer.AudioAssetMap
        let (audioPlayer, optAssetMap) =
            match optAssetMap with
            | Some _ -> (audioPlayer, Map.tryFind packageName audioPlayer.AudioAssetMap)
            | None ->
                note <| "Loading audio package '" + packageName + "' for asset '" + assetName + "' on the fly."
                let audioPlayer = tryLoadAudioPackage packageName audioPlayer
                (audioPlayer, Map.tryFind packageName audioPlayer.AudioAssetMap)
        (audioPlayer, Option.bind (fun assetMap -> Map.tryFind assetName assetMap) optAssetMap)

    let private playSong playSongMessage audioPlayer =
        let song = playSongMessage.Song
        let (audioPlayer', optAudioAsset) = tryLoadAudioAsset song.PackageName song.SongAssetName audioPlayer
        match optAudioAsset with
        | Some (WavAsset _) -> note <| "Cannot play wav file as song '" + string song + "'."
        | Some (OggAsset oggAsset) ->
            ignore <| SDL_mixer.Mix_VolumeMusic (int <| playSongMessage.Volume * single SDL_mixer.MIX_MAX_VOLUME)
            ignore <| SDL_mixer.Mix_PlayMusic (oggAsset, -1)
        | None -> note <| "PlaySongMessage failed due to unloadable assets for '" + string song + "'."
        { audioPlayer' with OptCurrentSong = Some playSongMessage }

    let private handleHintAudioPackageUse (hintPackageUse : HintAudioPackageUseMessage) audioPlayer =
        tryLoadAudioPackage hintPackageUse.PackageName audioPlayer

    let private handleHintAudioPackageDisuse (hintPackageDisuse : HintAudioPackageDisuseMessage) audioPlayer =
        let packageName = hintPackageDisuse.PackageName
        let optAssets = Map.tryFind packageName audioPlayer.AudioAssetMap
        match optAssets with
        | Some assets ->
            // all sounds / music must be halted because one of them might be playing during unload
            // (which is very bad according to the API docs).
            haltSound ()
            for asset in Map.toValueList assets do
                match asset with
                | WavAsset wavAsset -> SDL_mixer.Mix_FreeChunk wavAsset
                | OggAsset oggAsset -> SDL_mixer.Mix_FreeMusic oggAsset
            { audioPlayer with AudioAssetMap = Map.remove packageName audioPlayer.AudioAssetMap }
        | None -> audioPlayer

    let private handlePlaySound playSoundMessage audioPlayer =
        let sound = playSoundMessage.Sound
        let (audioPlayer, optAudioAsset) = tryLoadAudioAsset sound.PackageName sound.SoundAssetName audioPlayer
        match optAudioAsset with
        | Some (WavAsset wavAsset) ->
            ignore <| SDL_mixer.Mix_VolumeChunk (wavAsset, int <| playSoundMessage.Volume * single SDL_mixer.MIX_MAX_VOLUME)
            ignore <| SDL_mixer.Mix_PlayChannel (-1, wavAsset, 0)
        | Some (OggAsset _) -> note <| "Cannot play ogg file as sound '" + string sound + "'."
        | None -> note <| "PlaySoundMessage failed due to unloadable assets for '" + string sound + "'."
        audioPlayer

    let private handlePlaySong playSongMessage audioPlayer =
        if SDL_mixer.Mix_PlayingMusic () = 1 then
            if audioPlayer.OptCurrentSong = Some playSongMessage then audioPlayer
            else
                if  playSongMessage.TimeToFadeOutSongMs <> 0 &&
                    not (SDL_mixer.Mix_FadingMusic () = SDL_mixer.Mix_Fading.MIX_FADING_OUT) then
                    ignore <| SDL_mixer.Mix_FadeOutMusic playSongMessage.TimeToFadeOutSongMs
                else
                    ignore <| SDL_mixer.Mix_HaltMusic ()
                { audioPlayer with OptNextPlaySong = Some playSongMessage }
        else playSong playSongMessage audioPlayer

    let private handleFadeOutSong timeToFadeOutSongMs audioPlayer =
        if SDL_mixer.Mix_PlayingMusic () = 1 then
            if  timeToFadeOutSongMs <> 0 &&
                not (SDL_mixer.Mix_FadingMusic () = SDL_mixer.Mix_Fading.MIX_FADING_OUT) then
                ignore <| SDL_mixer.Mix_FadeOutMusic timeToFadeOutSongMs
            else
                ignore <| SDL_mixer.Mix_HaltMusic ()
        audioPlayer

    let private handleStopSong audioPlayer =
        if SDL_mixer.Mix_PlayingMusic () = 1 then ignore <| SDL_mixer.Mix_HaltMusic ()
        audioPlayer

    let private handleReloadAudioAssets audioPlayer =
        let oldAssetMap = audioPlayer.AudioAssetMap
        let audioPlayer = { audioPlayer with AudioAssetMap = Map.empty }
        List.fold
            (fun audioPlayer packageName -> tryLoadAudioPackage packageName audioPlayer)
            audioPlayer
            (Map.toKeyList oldAssetMap)

    let private handleAudioMessage audioPlayer audioMessage =
        match audioMessage with
        | HintAudioPackageUseMessage hintPackageUse -> handleHintAudioPackageUse hintPackageUse audioPlayer
        | HintAudioPackageDisuseMessage hintPackageDisuse -> handleHintAudioPackageDisuse hintPackageDisuse audioPlayer
        | PlaySoundMessage playSoundMessage -> handlePlaySound playSoundMessage audioPlayer
        | PlaySongMessage playSongMessage -> handlePlaySong playSongMessage audioPlayer
        | FadeOutSongMessage timeToFadeSongMs -> handleFadeOutSong timeToFadeSongMs audioPlayer
        | StopSongMessage -> handleStopSong audioPlayer
        | ReloadAudioAssetsMessage -> handleReloadAudioAssets audioPlayer

    let private handleAudioMessages (audioMessages : AudioMessage rQueue) audioPlayer =
        List.fold handleAudioMessage audioPlayer (List.rev audioMessages)

    let private tryUpdateCurrentSong audioPlayer =
        if SDL_mixer.Mix_PlayingMusic () = 1 then audioPlayer
        else { audioPlayer with OptCurrentSong = None }

    let private tryUpdateNextSong audioPlayer =
        match audioPlayer.OptNextPlaySong with
        | Some nextPlaySong ->
            if SDL_mixer.Mix_PlayingMusic () = 0 then
                let audioPlayer = handlePlaySong nextPlaySong audioPlayer
                { audioPlayer with OptNextPlaySong = None }
            else audioPlayer
        | None -> audioPlayer

    let private updateAudioPlayer audioPlayer =
        audioPlayer |>
            tryUpdateCurrentSong |>
            tryUpdateNextSong

    /// 'Play' the audio system. Must be called once per frame.
    let play audioMessages audioPlayer =
        let audioPlayer = handleAudioMessages audioMessages audioPlayer
        updateAudioPlayer audioPlayer

    /// Make an AudioPlayer.
    let makeAudioPlayer assetGraphFileName =
        { AudioContext = ()
          AudioAssetMap = Map.empty
          OptCurrentSong = None
          OptNextPlaySong = None
          AssetGraphFileName = assetGraphFileName }
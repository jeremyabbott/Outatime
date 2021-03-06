﻿module TraverseTests

open Outatime
open Xunit
open Bdd

type RoomCode = RoomCode of string

type Opening = Opened | Closed

type Availability = 
    | Opened of int
    | Closed

type RateCode = 
    | RateCode of string
    | AllRate

type Price = Price of decimal

type Rate = 
    { rateCode : RateCode
      prices : Price Temporal }

type Room = 
    { roomCode : RoomCode
      availabilities : Availability Temporal
      rates : Rate seq }

let jan15 d = System.DateTime(2015, 1, d, 0, 0, 0, System.DateTimeKind.Utc)

module Repartition = 
    let fullStock avail _ _ = avail
    
    //Here it is the simplest repartition, in addition, add closed and max sales on rates... (there is a bug, find it!)
    let rateLevel avail n i = 
        match avail with
        | Closed -> Closed
        | Opened a -> 
            let (stock, remain) = a / n, (a % n)
            stock + (max 0 (remain - i)) |> Opened

module Partner = 
    type Allotment = Allotment of int
    
    type PartnerRate = 
        | Opened of RoomCode * RateCode * Price * Allotment
        | Closed of RoomCode * RateCode

    let transpose availRepartition (roomCode, availabilityO) prices = 
        let pricesList = prices |> Map.toList
        let numberOfRate = prices |> Map.toList |> List.sumBy(fun (_,v) -> v |> Option.toList |> List.length)
        seq {
            match availabilityO, numberOfRate with
            | None, 0 -> yield! Seq.empty //All None
            | None, _ | Some _, 0 -> yield Closed (roomCode, RateCode.AllRate)
            | Some availability, _ ->
                yield!
                    pricesList
                    |> Seq.mapi(fun i (c, pO) -> 
                        match pO with
                        | None -> Closed (roomCode, c)
                        | Some p -> 
                            match i |> availRepartition availability numberOfRate with
                            | Availability.Closed -> Closed (roomCode, c)
                            | Availability.Opened allot -> Opened (roomCode, c, p, (Allotment allot))) } |> Seq.toList

    let toRequest state p v = 
        seq {
            yield! state
            let toString (date:DateTime) = date.ToString("yyyy/MM/dd")
            let start = p |> start |> toString
            let enD = p |> enD |> toString

            let toR = function
                | Closed (roomCode, rateCode) ->
                    sprintf "[%s; %s[ => %A, %A = Closed" start enD roomCode rateCode 
                | Opened (roomCode, rateCode, price, allot) ->
                    sprintf "[%s; %s[ => %A, %A = Opened %A/%A" start enD roomCode rateCode allot price
            yield! v |> Seq.map toR }


let transposeRoom repartition room = 
    let transposeRate rates = 
        rates 
        |> Seq.map(fun r -> r.rateCode, r.prices |> Outatime.contiguous) 
        |> Map.ofSeq 
        |> Outatime.ofMap
    
    let roomWithRoomCode = room.availabilities |> Outatime.contiguous |> Outatime.lift (function Some v -> (room.roomCode, Some v) | None -> (room.roomCode, None))

    let rates = room.rates |> transposeRate

    Partner.transpose repartition 
    <!> roomWithRoomCode
    <*> rates

let single = 
    { roomCode = RoomCode "SGL"
      availabilities = 
        ([ jan15  1 => jan15 5 := Opened 10 
           jan15  5 => jan15 10 := Opened 10 
           jan15 10 => jan15 25 := Closed
           jan15 27 => jan15 30 := Opened 20 ] |> Outatime.build)
      rates = 
        [ { rateCode= RateCode "RO"
            prices= 
                [ jan15  1 => jan15 10 := Price 120m 
                  jan15 10 => jan15 28 := Price 115m ] |> Outatime.build}
          { rateCode= RateCode "BB"; prices= ([ jan15 1 => jan15 15 := Price 135m ] |> Outatime.build) }] }

let double = 
    { roomCode = RoomCode "DBL"
      availabilities = 
        ([ jan15 1 => jan15  8 := Opened 5
           jan15 8 => jan15 23 := Opened 7 ] |> Outatime.build)
      rates = 
        [ { rateCode= RateCode "RO"; prices= ([ jan15 1 => jan15 25 := Price 240m ] |> Outatime.build) } 
          { rateCode= RateCode "BB"; prices= ([ jan15 1 => jan15 25 := Price 270m ] |> Outatime.build) }] }

[<Fact>]
let ``tranpose avp model to partner model with rate level repartition`` ()=
    When 
        [ single
          double ]
        |> Seq.map (transposeRoom Repartition.rateLevel)
        |> Seq.collect (Outatime.merge >> Outatime.fold Partner.toRequest Seq.empty)
        |> Seq.toList
    |> Expect 
        [ @"[2015/01/01; 2015/01/10[ => RoomCode ""SGL"", RateCode ""BB"" = Opened Allotment 5/Price 135M"
          @"[2015/01/01; 2015/01/10[ => RoomCode ""SGL"", RateCode ""RO"" = Opened Allotment 5/Price 120M"
          @"[2015/01/10; 2015/01/25[ => RoomCode ""SGL"", RateCode ""BB"" = Closed"
          @"[2015/01/10; 2015/01/25[ => RoomCode ""SGL"", RateCode ""RO"" = Closed"
          @"[2015/01/25; 2015/01/27[ => RoomCode ""SGL"", AllRate = Closed"
          @"[2015/01/27; 2015/01/28[ => RoomCode ""SGL"", RateCode ""BB"" = Closed"
          @"[2015/01/27; 2015/01/28[ => RoomCode ""SGL"", RateCode ""RO"" = Opened Allotment 20/Price 115M"
          @"[2015/01/28; 2015/01/30[ => RoomCode ""SGL"", AllRate = Closed"
          @"[2015/01/01; 2015/01/08[ => RoomCode ""DBL"", RateCode ""BB"" = Opened Allotment 3/Price 270M"
          @"[2015/01/01; 2015/01/08[ => RoomCode ""DBL"", RateCode ""RO"" = Opened Allotment 2/Price 240M"
          @"[2015/01/08; 2015/01/23[ => RoomCode ""DBL"", RateCode ""BB"" = Opened Allotment 4/Price 270M"
          @"[2015/01/08; 2015/01/23[ => RoomCode ""DBL"", RateCode ""RO"" = Opened Allotment 3/Price 240M"
          @"[2015/01/23; 2015/01/25[ => RoomCode ""DBL"", AllRate = Closed" ]
﻿module Outatime

type DateTime = System.DateTime

type TimeSpan = System.TimeSpan
    
type Period = 
    { StartDate : DateTime
      EndDate : DateTime }
    override this.ToString() = 
        let toString (date:DateTime) = date.ToString("yyyy/MM/dd")
        sprintf "[%s; %s[" (this.StartDate |> toString) (this.EndDate |> toString)

type Temporary<'v> = 
    { Period : Period
      Value : 'v }
    override this.ToString() = sprintf "%O = %O" this.Period this.Value

type Temporal<'v> = private Temporal of 'v Temporary seq

let ret x = 
    { Period = 
          { StartDate = DateTime.MinValue
            EndDate = DateTime.MaxValue }
      Value = x }
    |> Seq.singleton
    |> Temporal

let (=>) s e = 
    { StartDate = s
      EndDate = e }

let (:=) p v = 
    { Period = p
      Value = v }

let lift f (Temporal x) = x |> Seq.map(fun i -> i.Period := f i.Value) |> Temporal

let lift2 f (Temporal x) (Temporal y) = 
    seq {
        use xe = x.GetEnumerator()
        use ye = y.GetEnumerator()

        if(xe.MoveNext() && ye.MoveNext()) then
            let rec next () = 
                seq { 
                    if xe.Current.Period.StartDate < ye.Current.Period.EndDate then
                        let start = max ye.Current.Period.StartDate xe.Current.Period.StartDate
                        let enD = min ye.Current.Period.EndDate xe.Current.Period.EndDate
                        if start < enD then yield (start => enD := (f xe.Current.Value ye.Current.Value))
                        else failwithf "found not contiguous %A %A" enD start
                    let n = if ye.Current.Period.EndDate < xe.Current.Period.EndDate then ye.MoveNext() else xe.MoveNext()
                    if n then yield! next ()
                }

            yield! next ()
    } |> Temporal

let apply f x = lift2 (fun f x -> f x) f x

let map f x = apply (ret f) x

let private contiguousT zero f (temporaries:#seq<Temporary<_>>) = 
    seq { 
        use e = temporaries.GetEnumerator()
        if e.MoveNext() then 
            if e.Current.Period.StartDate <> DateTime.MinValue then 
                yield DateTime.MinValue => e.Current.Period.StartDate := None
            yield e.Current.Period := Some e.Current.Value
            let rec next previous = 
                seq { 
                    if e.MoveNext() then 
                        if e.Current.Period.StartDate > previous.Period.EndDate then 
                            yield previous.Period.EndDate => e.Current.Period.StartDate := None
                        yield e.Current.Period := f e.Current.Value
                        yield! next e.Current
                    elif previous.Period.EndDate <> DateTime.MaxValue then 
                        yield previous.Period.EndDate => DateTime.MaxValue := zero
                }
            yield! next e.Current
        else yield DateTime.MinValue => DateTime.MaxValue := None }

let merge (Temporal t) =
    seq {
        use e = t.GetEnumerator()

        if e.MoveNext() then
            let rec merge x =     
                seq { 
                    if e.MoveNext() then
                        let y = e.Current
                        if y.Period.StartDate <= x.Period.EndDate && y.Value = x.Value then
                            let start = min y.Period.StartDate x.Period.StartDate
                            let enD = max y.Period.EndDate x.Period.EndDate
                            if enD > start then yield! merge (start => enD := x.Value)
                            else yield x; yield! merge y
                        else yield x; yield! merge y
                    else yield x }
            yield! merge e.Current } |> Temporal

let private sort temporaries = temporaries |> Seq.sortBy (fun t -> t.Period.StartDate)

let toList (Temporal temporaries) = temporaries |> Seq.toList

let private removeEmpty temporaries = temporaries |> Seq.filter(fun t -> t.Period.StartDate < t.Period.EndDate)

let private check temporaries = temporaries |> Seq.map(fun t -> if t.Period.EndDate < t.Period.StartDate then failwithf "invalid period %O" t.Period else t)

let build temporaries = temporaries |> removeEmpty |> check |> sort |> Temporal
let contiguous zero f temporaries = temporaries |> removeEmpty |> check |> sort |> contiguousT zero f |> Temporal
let contiguousO temporaries = contiguous None Some temporaries

let ofMap temporals = 
    let folder state k t = lift2 (fun m i -> match i with Some v -> m |> Map.add k v | None -> m) state t

    Map.fold folder (ret Map.empty) temporals

let split length (Temporal temporaries) = 
    let duration p = p.EndDate - p.StartDate
    let rec split t = 
        seq{
            if t.Period |> duration <= length then yield t
            else
                let next = t.Period.StartDate + length
                yield { t with Period = { t.Period with EndDate = next } }
                yield! split { t with Period = { t.Period with StartDate = next } }
        }
    temporaries
    |> Seq.collect split
    |> Temporal

let clamp period (Temporal temporaries) = 
    temporaries
    |> Seq.collect(fun t -> 
        let start = max period.StartDate t.Period.StartDate
        let enD = min period.EndDate t.Period.EndDate
        if enD > start then 
            (start => enD := t.Value) |> Seq.singleton
        else Seq.empty)
    |> Temporal

let (<!>) = map
let (<*>) = apply
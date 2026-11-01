# MediaRenamer

MediaRenamer è un programma per **riconoscere e rinominare automaticamente film, serie TV, anime e sottotitoli**.
Prende file con nomi disordinati come `Breaking.Bad.S01E02.720p.HDTV-GRP.mkv` e li trasforma in nomi ordinati come
`Breaking Bad/Season 01/Breaking Bad - S01E02 - Cat's in the Bag.mkv`, usando i dati dei database online
[TMDB](https://www.themoviedb.org/) e [AniDB](https://anidb.net/).

È scritto in C# (.NET 8) con interfaccia Avalonia. Oggi funziona su **Windows**; il codice è pensato per poter girare anche su Linux, ma la versione Linux non è mai stata provata.

---

## Indice

1. [Cosa sa fare](#cosa-sa-fare)
2. [Installazione](#installazione)
3. [Primo avvio: configurare i provider](#primo-avvio-configurare-i-provider)
4. [Come si usa](#come-si-usa)
5. [Formati e binding](#formati-e-binding)
6. [Compilare il programma dal codice](#compilare-il-programma-dal-codice)
7. [Eseguire i test](#eseguire-i-test)
8. [Creare l'installer e la versione portabile](#creare-linstaller-e-la-versione-portabile)
9. [Aggiungere nuovi provider](#aggiungere-nuovi-provider)
10. [Struttura del progetto](#struttura-del-progetto)
11. [Problemi comuni](#problemi-comuni)

---

## Cosa sa fare

- **Riconosce** film, serie, anime e sottotitoli partendo dal nome del file.
- **Rinomina** i file in base a un formato che scegli tu, con anteprima e conferma prima di toccare qualunque file.
- **Non sovrascrive mai** file esistenti e permette di **annullare** (undo) l'ultima rinomina.
- **Sposta** i file in una cartella libreria, creando le sottocartelle (ad esempio una per serie e una per stagione), oppure li rinomina sul posto.
- **Episodi multipli**: gestisce file che contengono più episodi (`S01E01-02`, `Ep_01-02`).
- **Cerca episodi** (Search Episodes): cerchi una serie, vedi stagioni ed episodi e li associ ai file.
- **Correzione manuale** (Edit Match): se il riconoscimento sbaglia, scegli tu la serie, l'episodio o il film giusto.
- **Riordino** delle righe con le frecce o trascinandole, utile per associare i file agli episodi nell'ordine giusto.
- **Sottotitoli**: vengono collegati al loro video e seguono il suo nome (lingua e tipo, come `.it.forced.srt`, si mantengono).
- **Multilingua**: la lingua usata per *cercare* e quella usata per i nomi *prodotti* sono indipendenti. Puoi cercare con un titolo giapponese e ottenere nomi in italiano, con ripiego sull'inglese.
- **Editor dei formati** con anteprima in tempo reale, colori (i binding in blu, il testo che resta nel nome in arancione), elenco dei binding disponibili e suggerimenti.
- **Esplora binding** (Binding Inspector): mostra il valore di ogni binding per una serie o un film reale.
- **Finestre che ricordano** dimensione e posizione, e restano sempre visibili sullo schermo.
- **Provider a plugin**: si possono aggiungere nuove fonti di dati senza modificare il programma.

## Installazione

### Opzione A: installer (consigliata)
1. Vai nella sezione **Releases** della pagina GitHub e scarica `MediaRenamer-Setup.exe`.
2. Avvia il file e segui la procedura. Non servono permessi di amministratore.
3. Apri MediaRenamer dal menu Start (o dal collegamento sul desktop, se lo hai scelto).

Non serve installare .NET: è già incluso.

### Opzione B: versione portabile
1. Scarica `MediaRenamer-portable.zip` dalla sezione **Releases**.
2. Estrai lo zip dove vuoi (anche su una chiavetta USB).
3. Avvia `MediaRenamer.App.exe`.

Nella versione portabile impostazioni e cache restano nella cartella `userdata` accanto al programma. Per usare invece la cartella normale di Windows, elimina il file `portable.txt`.

### Opzione C: dal codice sorgente
Vedi [Compilare il programma dal codice](#compilare-il-programma-dal-codice).

### Dove salva i dati
Nella versione installata: `%LOCALAPPDATA%\MediaRenamer\` (digitalo nella barra di Esplora risorse). Contiene le sottocartelle `config` (impostazioni), `cache`, `data` e `plugins`.

## Primo avvio: configurare i provider

I provider sono le fonti dei dati. Si configurano in **Preferences**.

### TMDB (film e serie)
1. Crea un account gratuito su [themoviedb.org](https://www.themoviedb.org/).
2. Vai in *Impostazioni → API* e richiedi una chiave. Ti serve il **"API Read Access Token"**.
3. In MediaRenamer apri **Preferences**, scheda TMDB, incolla il token e attiva il provider.
4. Usa il pulsante di prova connessione per verificare.

TMDB è usato sia per i film (Movie Mode) sia per le serie (Episode Mode): sono due modalità separate dello stesso provider.

### AniDB (anime)
AniDB richiede un *client name* registrato su anidb.net. Inseriscilo in Preferences, scheda AniDB.

> Il token è salvato **cifrato** sul tuo computer (per l'utente Windows corrente). Non viene mai scritto nei file di configurazione né nel codice.

## Come si usa

1. **Carica i file**: usa il pulsante **Load**. A sinistra vedi i nomi originali.
2. **Scegli la fonte**: apri il menu **Fetch Data** (o **Match**) e scegli provider e modalità, ad esempio *TMDB – Episode Mode* per una serie. Il programma non sceglie mai il provider al posto tuo e non passa da solo a un altro se non trova nulla.
3. **Controlla l'anteprima**: a destra, in *New Names*, vedi i nuovi nomi. Le cartelle hanno colori diversi per livello, così si distinguono dal nome del file. Le righe incerte sono segnalate.
4. **Correggi se serve**: con **Edit** (o doppio clic su un nuovo nome) scegli a mano serie, episodio o film. Con **Search Episodes** associ gli episodi cercandoli direttamente.
5. **Scegli il formato**: con **Edit Format** modifichi come devono essere composti i nomi ([vedi sotto](#formati-e-binding)).
6. **Rinomina**: premi **Rename**. Si apre una finestra di conferma con tutte le modifiche; confermi e i file vengono rinominati. Se un file con lo stesso nome esiste già, quel file viene saltato, mai sovrascritto.
7. **Annulla**: se ti accorgi di un errore, usa **Undo last rename** per rimettere i nomi originali dell'ultima rinomina.

### Match Preferences
Nel menu di Fetch Data trovi **Match Preferences**, dove scegli la *lingua dei nomi prodotti*, l'ordine degli episodi (ad esempio ordine di trasmissione o ordine DVD, se il provider li offre) e altre opzioni di riconoscimento.

## Formati e binding

Un **formato** è un modello che descrive il nome finale. Dentro le parentesi graffe `{ }` metti dei **binding**, cioè segnaposto che il programma sostituisce con i dati veri.

| Formato | Risultato |
|---|---|
| `{n} - {s00e00} - {t}` | `Breaking Bad - S01E02 - Cat's in the Bag` |
| `{n}/Season {s00}/{n} - {s00e00} - {t}` | cartella serie, cartella stagione, poi il file |
| `{n} ({y})` | `Inception (2010)` |

Binding utili:

| Binding | Significato |
|---|---|
| `{n}` | nome della serie o del film |
| `{y}` | anno |
| `{s}`, `{s00}` | numero di stagione (la seconda forma con due cifre) |
| `{e}`, `{e00}` | numero di episodio |
| `{s00e00}` | stagione ed episodio insieme (`S01E02`) |
| `{t}` | titolo dell'episodio |
| `{drive}` | lettera dell'unità, ad esempio `C:` |

Il testo fuori dalle graffe resta così com'è. Il carattere `/` crea sottocartelle. Puoi anche applicare funzioni, ad esempio `{n.upper()}` mette il nome in maiuscolo.

Nell'editor trovi l'elenco completo dei binding (clic su un nome per inserirlo), l'anteprima in tempo reale e, nella scheda *Values*, il valore di ogni binding su dati reali.

I binding leggono sempre lo stesso modello di dati interno, quindi **funzionano uguale con qualsiasi provider**.

## Compilare il programma dal codice

Ti serve solo il [.NET SDK 8](https://dotnet.microsoft.com/download) o successivo. Poi, da un terminale nella cartella del progetto:

```powershell
dotnet build
dotnet run --project src/MediaRenamer.App
```

La prima riga compila tutto, la seconda avvia l'applicazione.

> Se compili mentre il programma è aperto, chiudilo prima: Windows blocca i file in uso.

## Eseguire i test

```powershell
dotnet test
```

Per eseguire solo un gruppo, ad esempio quello dell'interfaccia:

```powershell
dotnet test tests/MediaRenamer.App.Tests
```

Un test di integrazione con TMDB viene **saltato** se non hai impostato la variabile d'ambiente `MEDIARENAMER_TMDB_TOKEN` con il tuo token TMDB. Tutti gli altri test funzionano senza rete e senza credenziali. Il token non deve mai essere scritto nel codice o nel repository.

## Creare l'installer e la versione portabile

```powershell
.\scripts\publish.ps1
```

Crea nella cartella `dist`:
- `MediaRenamer\` – il programma autonomo (un solo `.exe`, .NET incluso);
- `MediaRenamer-portable.zip` – la versione portabile;
- `MediaRenamer-Setup.exe` – l'installer, **solo se** è installato [Inno Setup](https://jrsoftware.org/isinfo.php) (`iscc.exe`).

Opzioni: `-Version 1.0.0` imposta il numero di versione, `-Runtime linux-x64` pubblica per Linux (senza installer né zip; non provato).

L'icona dell'applicazione è `src/MediaRenamer.App/Assets/MediaRenamer.ico`; per rigenerarla, `scripts/make-icon.ps1`.

## Aggiungere nuovi provider

Un provider è una fonte di dati (come TMDB o AniDB). Il programma è fatto a plugin: puoi aggiungerne di nuovi **senza modificare il resto del codice**. Serve saper programmare in C#.

### Cosa deve fare un provider
Un provider cerca titoli e restituisce i dati già convertiti nel **modello interno** del programma (`MediaRenamer.Model`): in questo modo i binding e i formati funzionano subito, senza altro lavoro.

Esistono due tipi di provider:
- `IMovieProvider`: cerca film (`SearchMovieAsync`) e ne legge i dettagli (`GetMovieAsync`);
- `IEpisodeProvider`: cerca serie (`SearchSeriesAsync`), legge i dettagli (`GetSeriesAsync`) e gli episodi (`GetEpisodesAsync`).

Un plugin può offrirne uno o entrambi (TMDB offre entrambi). I contratti sono in `src/MediaRenamer.Plugin.Abstractions/Contracts.cs`.

### Passo per passo
1. Crea una libreria di classi .NET 8:
   ```powershell
   dotnet new classlib -n MioProvider -f net8.0
   ```
2. Aggiungi i riferimenti ai progetti `MediaRenamer.Plugin.Abstractions` e `MediaRenamer.Model` (percorso a quei `.csproj` nella cartella `src`).
3. Scrivi la classe del plugin:
   ```csharp
   using MediaRenamer.Plugin.Abstractions;

   public sealed class MioPlugin : IProviderPlugin
   {
       public string Id => "mioprovider";
       public string Name => "Il mio provider";
       public Version Version => new(1, 0);
       public int ApiVersion => PluginApi.Version;

       public IEnumerable<IMediaProvider> CreateProviders(IPluginContext context)
       {
           var http = context.CreateHttpClient(Id, new HttpOptions());
           yield return new MioEpisodeProvider(http);   // implementa IEpisodeProvider
       }
   }
   ```
4. Implementa il provider (`IMovieProvider` e/o `IEpisodeProvider`):
   - `ProviderId`, `DisplayName`, `Mode` (Movie o Episode) e `Capabilities` dicono al programma cosa sai fare;
   - `ConfigSchema` elenca i campi di configurazione (token, nome client, ecc.): l'app genera da sola il modulo in **Preferences** e salva i dati segreti cifrati;
   - `TestConnectionAsync` serve al pulsante di prova connessione;
   - nelle ricerche restituisci titoli in tutte le lingue disponibili: il riconoscimento multilingua dipende da questo.
   Un buon esempio completo da copiare è la cartella `src/MediaRenamer.Providers.Tmdb`.
5. Compila: `dotnet build -c Release`.
6. Crea questa cartella (aprila con Win+R scrivendo `%LOCALAPPDATA%\MediaRenamer\plugins`):
   ```
   plugins\
     MioProvider\
       MioProvider.dll
       plugin.json
       (eventuali altre DLL richieste)
   ```
   `plugin.json` contiene:
   ```json
   { "id": "mioprovider", "entry": "MioProvider.dll" }
   ```
   Nella versione portabile la cartella è `userdata\plugins` accanto al programma.
7. Riavvia MediaRenamer: il provider compare in **Preferences** e nei menu **Fetch Data** e **Search Episodes**. Se il caricamento fallisce, il plugin viene semplicemente ignorato e il programma parte comunque.

### Regole da rispettare
- Il provider **non conosce i binding**: restituisce solo dati del modello interno.
- Non esistono priorità o ripieghi automatici fra provider: è sempre l'utente a scegliere.
- Per le richieste web usa il client fornito dal programma (`context.CreateHttpClient`): gestisce limiti di velocità e nuovi tentativi. Per la cache usa `context.Cache`.

> **Sicurezza**: un plugin è codice .NET con gli stessi permessi del programma. Installa solo plugin di cui ti fidi.

### Alternativa: provider incluso nel programma
Se vuoi distribuire il provider dentro l'installer, crea un progetto `MediaRenamer.Providers.Nome` come quelli di TMDB e AniDB, referenzialo da `MediaRenamer.App` e registralo all'avvio (vedi `src/MediaRenamer.App/Services/AppServices.cs`).

## Struttura del progetto

```
src/
  MediaRenamer.App/              interfaccia grafica (Avalonia)
  MediaRenamer.Core/             riconoscimento, abbinamento, rinomina e annullamento
  MediaRenamer.Bindings/         binding, formati e funzioni
  MediaRenamer.Model/            modello dati comune, indipendente dai provider
  MediaRenamer.Plugin.Abstractions/  contratti per i provider
  MediaRenamer.PluginHost/       caricamento dei plugin
  MediaRenamer.Infrastructure/   configurazione, cache, credenziali cifrate
  MediaRenamer.Platform/         parti dipendenti dal sistema operativo
  MediaRenamer.Providers.Tmdb/   provider TMDB
  MediaRenamer.Providers.AniDb/  provider AniDB
tests/                           test automatici di ogni progetto
scripts/                         publish.ps1 (installer/portabile), make-icon.ps1
installer/                       script di Inno Setup
```

## Problemi comuni

- **Nessun risultato dalla ricerca**: controlla in Preferences che il provider sia attivo e il token corretto, poi prova il pulsante di prova connessione.
- **Il nome proposto è sbagliato**: usa **Edit** per scegliere a mano il risultato giusto, oppure **Search Episodes**.
- **`dotnet build` dà errore sui file in uso**: chiudi MediaRenamer e riprova.
- **L'icona non cambia dopo l'installazione**: è la cache delle icone di Windows; riavvia Esplora risorse o ricrea il collegamento.
- **Ho rinominato per sbaglio**: usa l'annullamento (undo).

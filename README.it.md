# LHInstaller

Applicazione portatile che rimette in piedi i programmi di un PC appena
formattato. Si prepara una lista prima di formattare, si copia l'eseguibile su
una chiavetta, e dopo il formattaggio un solo pulsante scarica e installa tutto.

Non contiene installer al proprio interno: scarica ogni cosa al momento, dai
siti ufficiali, passando per il catalogo di **winget**.

*English: [README.md](README.md) · [README.txt](README.txt)*

![LHInstaller](docs/screenshot.png)

## Come si usa

**Prima di formattare**

1. **Leggi da questo PC** — due schede. La prima elenca quello che hai e che winget
   sa reinstallare da solo: spunti e basta. La seconda elenca quello che winget
   *non* sa rimettere (preso da un sito, con licenza, da un portale). Per quelli
   puoi cercare una corrispondenza a catalogo, incollare l'indirizzo dell'installer,
   aprire una ricerca nel browser, o metterli in lista come promemoria.
2. **Cerca nel catalogo** — scrivi un nome: la riga *Consigliata* è la versione
   stabile più recente, con beta e nightly spinte in fondo.
3. **Aggiungi indirizzo** — per ciò che nel catalogo non c'è.
4. Organizza in gruppi, spunta quello che vuoi, e copia la cartella su una chiavetta.
   Bastano due file: `LHInstaller.exe` e il `LHInstaller.json` che si scrive accanto.

**Dopo aver formattato**

Ricopi la cartella, doppio clic, premi **Avvia installazioni**. Una sola richiesta di
amministratore per tutta la sessione, poi scarica e installa in fila. La console
incorporata mostra l'output vero di winget, riga per riga; la colonna *Stato* dice a
che punto è ogni programma.

Il dettaglio d'uso completo è in [LEGGIMI.txt](LEGGIMI.txt).

## Perché C# e non Python

Il vincolo che decide tutto è: *deve funzionare su un PC appena formattato,
senza installare niente prima*. Su una installazione pulita di Windows 11 ci
sono sempre:

| | |
|---|---|
| .NET Framework 4.8 | sì, è un componente del sistema |
| winget | sì, arriva con "Programma di installazione app" |
| PowerShell 5.1 | sì |
| Python | **no** |

Con Python il primo passo sarebbe "scarica e installa Python", cioè proprio il
passo che l'applicazione dovrebbe eliminare. La versione *embeddable* di Python
non risolve, perché non include tkinter e quindi non ha interfaccia grafica.

Da qui: **C# / WinForms su .NET Framework 4.8**, un singolo `.exe` da ~230 KB.

## Come si compila

```powershell
.\build.ps1
```

Il compilatore usato è `csc.exe` di `C:\Windows\Microsoft.NET`, che c'è su ogni
Windows 10 e 11. Non servono Visual Studio né l'SDK di .NET. La conseguenza
piacevole è che LHInstaller si ricompila anche sul PC formattato che serve a
ripopolare. Lo script genera anche `app.ico` (lo stesso disegno dell'icona di
finestra, in `Icons.DrawAppBitmap`) e lo incorpora con `/win32icon`.

Il prezzo è che il compilatore in dotazione ferma il linguaggio a **C# 5**:
niente interpolazione di stringhe, niente `?.`, niente membri con corpo di
espressione. I sorgenti sono scritti di conseguenza, e devono restare
**puro ASCII** — i caratteri fuori dall'ASCII (glifi delle icone, simboli
nelle stringhe) vanno scritti come `"\uXXXX"`. `build.ps1` si ferma se trova
un byte non ASCII, perché il compilatore leggerebbe con la codepage di sistema
e compilerebbe male senza dirlo.

## Struttura

```
Program.cs          avvio, opzioni di riga di comando (--avvia, --apri)
app.manifest        asInvoker + DPI; l'elevazione avviene a richiesta
build.ps1           compilazione e generazione dell'icona

Core/
  Models.cs         AppItem, Profile, Groups, SearchResult, InstallOutcome
  Tr.cs             lingua: Tr.T(it, en) e Tr.F(it, en, ...)
  UpdateCheck.cs    controllo della versione dell'app su GitHub
  Json.cs           serializzazione senza librerie esterne, con indentazione
  Storage.cs        percorsi, caricamento e salvataggio del profilo, backup
  ProcessRunner.cs  esecuzione di processi con output riversato riga per riga
  Table.cs          lettura delle tabelle a colonne fisse stampate da winget
  Winget.cs         ricerca, installati, installazione, codici di uscita,
                    confronto versioni
  WingetFormat.cs   import/export nel formato di "winget export"
  DirectUrl.cs      scaricamento da indirizzo, firma remota, tipo di installer

UI/
  Theme.cs          tavolozza, caratteri, icone (glifi Segoe), renderer piatto
  MainForm.cs       finestra unica: gruppi | tabella, console sotto, azioni
  GroupNav.cs       pannello dei gruppi con caselle a tre stati
  ConsoleBox.cs     la console incorporata
  SearchForm.cs     ricerca nel catalogo
  ImportForm.cs     lettura del PC: reinstallabili / non reinstallabili
  UrlForm.cs        aggiunta e modifica di un indirizzo diretto
  HelpForm.cs       "Come funziona" e "Informazioni"
```

## Le decisioni tecniche non ovvie

### Leggere le tabelle di winget senza dipendere dalla lingua

`winget` stampa tabelle a colonne di larghezza fissa, e traduce le intestazioni
nella lingua di Windows. Cercare la colonna "Version" fallirebbe su un sistema
in italiano.

`Table.cs` ricava invece le posizioni delle colonne dalla riga di intestazione e
legge i valori **per posizione**, non per nome. Ogni parola dell'intestazione è
una colonna: le intestazioni di winget sono parole singole in tutte le lingue,
e quando una colonna è stretta quanto il suo titolo (`Name Id      Version`) la
separa dalla successiva **un solo spazio** — contare due spazi, com'era in una
prima versione, faceva sparire la colonna Id nelle tabelle piccole.

Il filtro del rumore in `ProcessRunner.cs` scarta lo spinner e le barre di
riempimento, ma lascia passare la riga di trattini che separa l'intestazione,
perché è quella che dice dove cominciano le colonne.

### Riconoscere la famiglia di un installer scaricato

Ogni famiglia usa un argomento diverso per installare senza finestre. Provarli
a tentativi significherebbe eseguire più volte lo stesso installer.

`DirectUrl.Detect` legge i primi e gli ultimi 2 MB del file scaricato, salta i
byte nulli così che anche il testo in UTF-16 diventi leggibile, e cerca le
firme: `Inno Setup`, `Nullsoft`, `wixburn`, `InstallShield`. Da lì ricava
l'argomento corretto. Se non riconosce nulla, apre la finestra dell'installer e
lo dichiara nella console, invece di fingere un'installazione silenziosa
riuscita.

### Icone senza file immagine

Le icone sono glifi di **Segoe Fluent Icons** (Windows 11) o **Segoe MDL2
Assets** (Windows 10), resi in bitmap a runtime da `Icons.Glyph`. Sono le
stesse icone delle app di sistema, nitide a ogni DPI, e non c'è un solo PNG da
portarsi dietro. Se mancano entrambi i caratteri, i pulsanti restano di solo
testo. I codepoint usati sono stati verificati a occhio uno per uno, non
presi dalla memoria.

### Stato e versioni

La colonna **Versione** è la versione a catalogo (quella che winget
installerebbe oggi); *Controlla aggiornamenti* la tiene al passo. Lo **Stato**
nasce dal confronto con quanto `winget list` riconosce sul PC: `installato`,
`da installare`, oppure `installato · ↑ vX disponibile` quando il catalogo è
più avanti. Il confronto è numerico per segmenti (`Winget.CompareVersions`),
non di stringa: un'installata 2.55.0.4 contro una catalogata 2.55.0.3 non è un
aggiornamento. Segmenti non numerici diversi (`g366879e1`) non fanno scattare
nulla: meglio tacere che inventare.

Per gli indirizzi diretti si confrontano `ETag`, `Last-Modified` e dimensione:
rilevano che **il file è cambiato**, non il numero di versione, che dall'esterno
non è conoscibile.

### Due lingue senza tabella di chiavi

Italiano e inglese stanno **uno accanto all'altro** nel punto d'uso:

```csharp
Tr.T("Salva", "Save")
Tr.F("{0} voci", "{0} entries", n)
```

Costa qualche carattere per riga, ma toglie il problema peggiore di questi impianti:
una chiave che cambia da un lato e resta vecchia dall'altro, e stringhe orfane che
nessuno si accorge di aver perso. I segnaposto sono quelli di `string.Format`, così
l'ordine delle parole può cambiare fra le due lingue senza toccare chi chiama.

La lingua va decisa **prima** di costruire le finestre — il testo dei controlli si
scrive una volta sola, alla creazione — quindi `Program.Main` la legge con
`Storage.PeekLanguage()`, una lettura minima del profilo che non può fallire in modo
rumoroso. Cambiarla a finestra aperta significherebbe riscrivere ogni controllo e
sperare di non dimenticarne nessuno: `ChangeLanguage` salva, riavvia il processo e
riapre. `Thread.CurrentCulture` segue la lingua scelta, non quella di Windows,
altrimenti un'interfaccia inglese mostrerebbe "1,6 MB" e date all'italiana.

Quello che **non** si traduce: i valori che finiscono nel profilo. I nomi dei gruppi
predefiniti sono salvati in italiano e tradotti solo quando si mostrano
(`Groups.Show`), gli esiti (`OK`, `ERRORE`, ...) restano codici interni, e
`(automatico)` / `(mostra la finestra)` hanno un valore canonico separato
dall'etichetta. Così un profilo preparato in inglese si apre in italiano, e viceversa.

### Il controllo della versione dell'app

`UpdateCheck` chiede a GitHub l'ultima release della repo del progetto
(`/repos/{owner}/{repo}/releases/latest`). Per una app portatile è il posto giusto:
basta pubblicare una release con il tag della versione (`v1.2`) e tutte le copie in
giro se ne accorgono al primo avvio, senza server da tenere acceso. `Owner` e `Repo`
sono campi statici, non costanti: se la repo cambia nome si tocca una riga.

Il confronto usa `Winget.CompareVersions`, lo stesso dei pacchetti: numerico per
segmenti, così `1.10` è più recente di `1.9`. Un 404 — repo assente o senza release —
non è un errore da mostrare: all'avvio tace, e a richiesta dice "non c'è ancora
nessuna versione pubblicata", che è la verità.

Provato sul campo contro una repo reale con release (`microsoft/winget-cli`): tag,
titolo, data, pagina e note in Markdown letti correttamente, e `Newer` acceso.

## Elevazione

Il manifesto chiede `asInvoker`: compilare la lista non richiede privilegi. Alla
pressione di *Avvia installazioni*, se il processo non è elevato,
l'applicazione salva il profilo e si riavvia con `runas` passando `--avvia`, e
riprende da sola. Una sola richiesta UAC per l'intera sessione, invece di una
per programma. Il pulsante porta lo scudo finché il processo non è elevato, e
la barra di stato dice sempre in che condizione si è.

### I non reinstallabili e i promemoria

`winget list` vede *tutto* ciò che è installato, ma sa reinstallare solo quello
che correla a un pacchetto del catalogo. Il resto — installati da un sito, con
licenza, dai portali dei produttori — ha identificativi `ARP\...` o `MSIX\...`
e nessuna origine. La seconda scheda di *Leggi da questo PC* li elenca,
classificati (`Winget.Classify`): app di Windows, giochi Steam, driver e
componenti sono nascosti di partenza; restano gli "installati da un setup".

Per ognuno: *Aggiungi con indirizzo…*, *Cerca il sito nel browser*, oppure
*Aggiungi come promemoria* — una voce senza URL nel gruppo **Da completare**,
con stato "manca l'indirizzo"; l'installazione la salta con un avviso finché
non ha un indirizzo (`AppItem.IsPlaceholder`).

*Cerca corrispondenze nel catalogo* interroga winget per nome, una riga alla
volta. La prima regola provata ("il nome inizia con…") sbagliava una volta su
due (DaVinci Resolve → un plugin "DaVinci Resolve RPC"); quella attuale
(`Winget.MatchStrength`) richiede l'uguaglianza esatta del nome normalizzato,
almeno 4 lettere, e distingue le corrispondenze **sicure** (l'editore
nell'identificativo compare nel nome: `Brave.Brave`, `Google.Chrome`) dai
**forse** (`Vortenia.Overwatch` per "Overwatch"), mostrati in arancione perché
decida l'utente. Sul PC di prova: 3 sicure, tutte giuste; 2 forse, una giusta.

## Cosa copre e cosa no

Il catalogo winget copre i programmi scaricabili dal web. Non copre quelli con
licenza personale o account — plugin audio, Ableton, FL Studio, DaVinci Resolve,
Native Access — che la scheda "Non reinstallabili" elenca, per aggiungerli come
indirizzo diretto, promemoria, o recuperarli dai portali dei produttori.

## Riga di comando

```
LHInstaller.exe --apri cerca|pc|indirizzo|aiuto|info   apre subito quel dialogo
LHInstaller.exe --avvia                                 parte con l'installazione
```

## Pubblicare un aggiornamento

1. Alza `AppInfo.Version` in `Core/Storage.cs`.
2. `.\build.ps1`.
3. Su GitHub, in `seremanuels-create/LHInstaller`, crea una release con tag `v<versione>`
   (per esempio `v1.2`), allega `dist\LHInstaller.exe` e scrivi le novità nel corpo:
   diventano il testo del pulsante "Novità".

Da quel momento ogni copia in circolazione, al primo avvio con una connessione, mostra
la striscia con la versione nuova.

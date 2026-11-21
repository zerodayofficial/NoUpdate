# NoUpdate — Play now, update later.

**NoUpdate** è un payload per PlayStation 5 progettato per neutralizzare il blocco **“aggiornamento richiesto”** in un ambiente modificato, **senza dipendere da una lista predefinita di giochi**. Combina un intervento immediato sul controllo in memoria con modifiche persistenti ai metadati dei titoli riconosciuti come bloccati.

> **La differenza fondamentale:** la patch in RAM agisce finché il payload rimane attivo; le modifiche ai metadati vengono salvate su disco e possono sopravvivere al riavvio. Non si tratta dello stesso intervento e nessuno dei due rende automaticamente compatibile un gioco che richiede funzionalità di firmware non presenti sulla console.

**Versione:** [v1.0.0](https://github.com/zerodayofficial/NoUpdate/releases/tag/v1.0.0) · **Autore:** ZER0DAY · **Licenza:** [GPL-3.0](LICENSE)

## Il problema che affronta

Alcuni titoli possono incontrare un controllo di sistema che segnala un aggiornamento obbligatorio prima dell'avvio. NoUpdate cerca di intervenire sulla verifica che produce quel blocco, anziché scaricare l'aggiornamento o intervenire sugli eseguibili dei giochi.

Il payload utilizza **due livelli complementari**:

| Livello | Come agisce | Durata |
| --- | --- | --- |
| **Sblocco in memoria** | Individua e modifica i punti del controllo di aggiornamento nel processo di sistema, verificando periodicamente la patch. | Finché il processo e il payload rimangono attivi; può essere riapplicato all'avvio successivo. |
| **Sblocco dei metadati** | Analizza i titoli installati e, quando necessario, modifica i campi di versione nei relativi `param.json`, dopo un backup. | I dati scritti restano sul disco anche dopo il riavvio, salvo rigenerazioni o modifiche successive. |

La scansione è **dinamica**: non esiste una whitelist di title ID. La versione attuale dispone però di un limite interno di **96 titoli analizzabili per esecuzione**.

## Compatibilità

- **Intervallo firmware previsto nel codice:** PS5 **6.xx–13.60**.
- **Firmware effettivamente testato:** **10.01**.
- **Ambiente richiesto:** console già predisposta all'esecuzione di payload compatibili, per esempio tramite OnionHEN.
- **Destinazione d'uso:** console di proprietà, giochi offline e single-player.

**Attenzione alla distinzione tra dichiarato e verificato.** Il codice riconosce l'intervallo 6.xx–13.60, ma questo **non equivale a un collaudo su ogni firmware incluso**. Al di fuori dell'intervallo viene registrato un avviso, senza interrompere automaticamente tutti i tentativi di intervento. L'esito dipende dalla versione del sistema e dalle condizioni della console.

## Cosa succede quando NoUpdate viene avviato

1. **Avvio e identificazione.** Scrive un messaggio di benvenuto nel kernel log e invia una notifica alla PS5.
2. **Controllo del firmware.** Legge la versione installata, registra il risultato e segnala se è fuori dall'intervallo previsto.
3. **Ricerca del controllo di aggiornamento.** Individua il processo di sistema interessato, cerca i punti riconosciuti e tenta la patch in memoria. Se una patch risulta già presente, la riconosce evitando una scrittura superflua.
4. **Predisposizione dell'autostart.** Se trova l'ELF nei percorsi OnionHEN previsti, cerca di creare il relativo marcatore di avvio automatico. **Non copia autonomamente l'ELF** e non garantisce l'autostart in ambienti che non rispettano queste condizioni.
5. **Configurazione DNS.** Tenta di applicare il DNS alternativo al profilo di rete disponibile e comunica l'esito. **Nella v1.0.0 questo tentativo è automatico, non disattivabile mediante un'opzione del payload.**
6. **Scansione della libreria.** Legge i metadati disponibili, esclude le voci che non riconosce come giochi e identifica le condizioni che possono causare il blocco.
7. **Intervento persistente.** Per ciascun titolo segnalato tenta il backup e la modifica dei file di metadati interessati, poi rilegge i campi scritti per verificarli.
8. **Resoconto.** Produce notifiche e messaggi di log con numero di giochi rilevati, problemi individuati e risultati delle operazioni.
9. **Sorveglianza della patch.** Se almeno un punto della patch in memoria è attivo, rimane in esecuzione e lo ricontrolla **ogni 5 secondi**. Se il controllo viene ripristinato, tenta di riapplicare la patch.

**Se la patch in memoria non viene trovata o applicata**, il payload segnala l'insuccesso e non avvia il ciclo di sorveglianza. Le altre fasi raggiunte dal programma possono comunque essere già state eseguite: non bisogna interpretare una singola notifica come prova che ogni intervento sia riuscito.

## 1. Sblocco immediato: intervento in memoria

NoUpdate cerca nel processo di sistema le sequenze compatibili con il controllo di aggiornamento. Quando trova un punto riconosciuto, tenta di neutralizzarlo e controlla il risultato rileggendo la memoria.

Il meccanismo è pensato per essere **globale rispetto al controllo individuato**: non richiede di applicare una patch separata per ogni title ID. Riconosce inoltre un punto già modificato per evitare riscritture non necessarie.

Durante l'esecuzione, il payload ricontrolla i punti individuati a intervalli di **5 secondi**. Se riscontra il ripristino delle istruzioni precedenti, prova a riapplicare la patch; se perde la possibilità di leggere il processo, interrompe la sorveglianza e registra l'evento.

Questa fase opera **sulla memoria del processo di sistema**, non sui file eseguibili installati dei videogiochi. Non è però permanente in RAM: dopo il riavvio il processo deve essere nuovamente modificato, se necessario.

## 2. Sblocco persistente: analisi e modifica dei metadati

La seconda fase esamina le informazioni dei titoli presenti nella directory `/user/appmeta`, identificando le directory con un formato di title ID riconosciuto e leggendo i relativi file `param.json`.

### Quali problemi riconosce

- **Requisito firmware:** il campo `requiredSystemSoftwareVersion` indica una versione superiore a quella della PS5.
- **Disallineamento di versione:** `targetContentVersion` e `contentVersion` differiscono, salvo i casi esclusi dal controllo del codice.

Il payload applica anche filtri su categoria, badge e alcuni identificativi di contenuto, per evitare di trattare indiscriminatamente come giochi le voci di sistema o altri contenuti. **Se non rileva le condizioni previste, non tenta la riscrittura del titolo.**

### Quali file può esaminare

Per un titolo interessato, il codice prova a lavorare su **fino a tre posizioni**:

- `/user/appmeta/<titleId>/param.json`
- `/user/app/<titleId>/sce_sys/param.json`
- `/user/patch/<titleId>/sce_sys/param.json`

**“Fino a tre” non significa che tutte siano sempre presenti o modificabili.** Le copie non leggibili o prive dei campi previsti vengono saltate. La disponibilità dei file varia a seconda del titolo e dello stato della console.

### Come viene protetto il file prima della modifica

Prima di scrivere un file, NoUpdate tenta di crearne una copia nella cartella:

`/data/NoUpdate/backup/<titleId>/`

I nomi dei backup identificano la provenienza: `appmeta.param.json`, `app.param.json` e `patch.param.json`. **Se il backup esiste già, il payload lo conserva e non lo sovrascrive.**

Se il backup necessario non riesce, la modifica di **quella copia** non viene effettuata. Dopo una scrittura il payload rilegge i byte interessati e registra l'esito. Un errore su un percorso non implica automaticamente che tutti gli altri siano stati modificati con successo.

### Che cosa viene cambiato

L'intervento è limitato ai campi di versione riconosciuti nel JSON, senza riscrivere l'intero documento:

- Se è stato rilevato un requisito firmware superiore, il codice tenta di allineare il valore del campo firmware alla versione corrente della console.
- Se è presente **solo** il problema di disallineamento del contenuto, può scrivere un valore sentinella nel campo `contentVersion`. Questa riscrittura non viene tentata nello stesso passaggio quando è attivo anche il flag di requisito firmware.

La modifica è **persistente a livello di file**, ma non è una garanzia assoluta contro una successiva ricostruzione dei metadati da parte del sistema. Né sostituisce librerie, funzioni o altri requisiti reali eventualmente mancanti su un firmware precedente.

## 3. DNS alternativo: comportamento della v1.0.0

NoUpdate include la configurazione di un DNS primario alternativo:

**`62.210.38.117`**

Il codice prova prima a individuare il profilo di rete attivo. Se non ottiene un risultato utile, tenta le impostazioni generali **Ethernet** e **Wi-Fi**.

Per limitare le modifiche non previste:

- legge il valore esistente prima di scrivere;
- procede se il campo è vuoto o presenta un formato simile a un indirizzo IPv4; il controllo del formato **non equivale a una validazione completa dell'indirizzo**;
- se trova già lo stesso DNS, non lo riscrive;
- tenta più letture di verifica dopo la modifica;
- comunica se il valore è stato impostato, se era già presente o se l'operazione non è riuscita.

**Importante:** se è presente un altro DNS primario in formato IPv4, il payload può sostituirlo. Nella build pubblicata **non è prevista una scelta interattiva per saltare questa fase**. La variazione può influire su risoluzione dei domini, privacy e connettività; l'effetto sulla rete può richiedere **riconnessione o riavvio**. L'indirizzo è quello incorporato nel codice, non un servizio gestito o garantito da questo README.

## 4. Autostart e continuità dopo il riavvio

NoUpdate prova a predisporre l'avvio automatico tramite OnionHEN **soltanto se trova l'ELF** in uno dei due percorsi che riconosce:

- `/user/data/OnionHEN/payloads/NoUpdate.elf`
- `/data/OnionHEN/payloads/NoUpdate.elf`

In questo caso controlla la presenza del relativo file `.auto_start` e, se assente, tenta di crearlo. Un marcatore già esistente viene lasciato invariato.

Questa funzione **non installa OnionHEN**, non trasferisce il payload e non assicura che ogni versione dell'ambiente supporti lo stesso comportamento. L'autostart permette di riprovare la patch volatile a un nuovo avvio; **i metadati già modificati sono invece una forma separata di persistenza**.

## 5. Notifiche e diagnostica

Il payload utilizza due canali complementari:

**Notifiche a schermo** per i risultati immediati:

- avvio del payload;
- DNS impostato, già attivo oppure non modificato;
- patch globale non applicata, attiva senza blocchi rilevati oppure attiva con elenco giochi non leggibile;
- numero di titoli con condizioni di blocco;
- nome e identificativo dei **primi 8 titoli** interessati, con segnalazione degli eventuali altri nel log;
- conteggio dei titoli per cui l'intervento sui metadati è stato riportato come riuscito.

**Kernel log (`klog`)** per la diagnosi approfondita:

- versione firmware, processo analizzato ed esito della ricerca dei punti di patch;
- scansione dei titoli, versioni richieste, flag **FW** e **UPD**;
- risultati di backup, scrittura e verifica per i diversi file di metadati;
- stato dei tentativi DNS e dell'autostart;
- perdita della patch in memoria e relativi tentativi di ripristino.

Le notifiche forniscono una sintesi, **non una certificazione di compatibilità di ogni gioco**. Per analizzare un risultato parziale o un errore, il log è la fonte più dettagliata.

## 6. Sicurezza, limiti e ripristino

**Misure presenti nel codice**

- Backup prima della scrittura dei metadati interessati.
- Conservazione dei backup già esistenti.
- Verifica in lettura dei campi modificati.
- Riconoscimento delle patch in RAM già applicate.
- Segnalazione dei fallimenti senza presentare ogni tentativo come riuscito.
- Nessuna procedura esplicita di cancellazione dei salvataggi o dei giochi.

**Limiti da conoscere**

- La scansione gestisce **massimo 96 titoli** per esecuzione.
- L'intervallo firmware dichiarato non rappresenta un test esaustivo; **10.01 è il firmware testato**.
- Alcuni file possono mancare, essere illeggibili oppure avere metadati inattesi.
- Un backup riuscito non rende impossibile un errore di scrittura, un ripristino successivo da parte del sistema o una perdita di dati dovuta ad altre cause.
- Non è implementato un **ripristino automatico**: i backup consentono un recupero manuale da valutare con attenzione.
- Il DNS viene tentato **automaticamente** e può modificare la configurazione esistente.
- L'intervento sulla richiesta di aggiornamento non rimuove le dipendenze tecniche effettive di un gioco.

Prima di utilizzare software che modifica metadati persistenti, è prudente conservare anche una copia indipendente dei dati importanti. La presenza di backup automatici non sostituisce una procedura di recovery verificata.

## Che cosa NoUpdate non fa

- Non scarica o installa aggiornamenti.
- Non modifica gli eseguibili dei giochi né fornisce contenuti di gioco.
- Non aggiunge funzioni firmware mancanti e **non garantisce l'avvio di ogni titolo**.
- Non fornisce licenze o strumenti per aggirare la loro verifica.
- Non richiede una lista preimpostata di title ID.
- Non include nella v1.0.0 un comando di ripristino automatico dei metadati.
- Non garantisce il funzionamento su ogni firmware dell'intervallo dichiarato.

## In sintesi

**NoUpdate affronta lo stesso problema su due piani diversi:** tenta di neutralizzare in tempo reale il controllo di aggiornamento e, separatamente, corregge i metadati dei giochi riconosciuti come bloccati, conservando una copia degli originali prima delle modifiche. In aggiunta gestisce notifiche e log, tenta l'autostart tramite OnionHEN e applica automaticamente il DNS configurato nella build.

La sua efficacia dipende dal firmware, dall'ambiente payload, dal formato dei metadati e dalle caratteristiche reali di ciascun titolo. **Testato su PS5 firmware 10.01; intervallo previsto nel codice 6.xx–13.60.**

## File del progetto

- [`src/Program.cs`](src/Program.cs) — sorgente C# del payload.
- [`src/NoUpdate.csproj`](src/NoUpdate.csproj) — definizione del progetto.
- [`release/NoUpdate.elf`](release/NoUpdate.elf) — binario della build pubblicata.
- [`release/SHA256SUMS.txt`](release/SHA256SUMS.txt) — impronta SHA-256 del binario.
- [`LICENSE`](LICENSE) — licenza GPL-3.0.

**Release:** [NoUpdate v1.0.0 e relativi file](https://github.com/zerodayofficial/NoUpdate/releases/tag/v1.0.0).

## Note legali e crediti

Progetto destinato all'uso sulla **propria console** in contesti **offline e single-player**. Distribuito **così com'è**, senza garanzie di funzionamento, compatibilità o assenza di effetti indesiderati. Chi lo utilizza è responsabile della configurazione della propria console e dei propri dati.

**Made by ZER0DAY** · [Supporto su Ko-fi](https://ko-fi.com/zzer0dayyy)

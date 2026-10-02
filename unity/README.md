# Vertical Impact Testsite — versione Unity

Serve Unity 6 (testato come codice per 6000.x) con **URP**.

## Come avviarlo
1. Unity Hub → **New project** → template **Universal 3D** → Create.
2. Copia la cartella `Assets/VITS` di questo repository dentro la cartella `Assets` del tuo progetto.
3. Apri una scena qualsiasi (va bene `SampleScene`) e premi **Play**.
   Il gioco costruisce da solo mappa, luci, giocatore e manichini
   (la camera e le luci del template vengono disattivate).
4. Clicca nella finestra Game per catturare il mouse.

## Creare il gioco (.exe)
1. Chiudi Play. Cancella la vecchia cartella `Assets/VITS` e metti quella nuova dello zip.
2. Aspetta che Unity finisca di compilare. Nella Console compare `VITS: build materials ready in Assets/VITS/Resources/BuildMaterials`
   (lo script `Assets/VITS/Editor/VITSBuildSetup.cs` crea da solo i materiali che portano gli shader nel gioco;
   c'è anche il menu **VITS > Prepare build** per rifarlo a mano).
3. **File > Build Profiles** → scegli **Windows** → controlla che nella **Scene List** ci sia la scena (es. `Scenes/SampleScene`, spuntata;
   se manca: **Add Open Scenes**).
4. **Build** → scegli una cartella vuota (es. `Build`) → dentro trovi il .exe da avviare.
5. All'avvio lo schermo resta nero 2-5 secondi mentre vengono generati corpo e livello: è normale.
   Se qualcosa va storto, in basso compare una riga rossa `ERROR: ...` (per più dettagli spunta **Development Build** prima di Build).

Perché prima era nero: il gioco crea i materiali dal codice e nella build Unity non include gli shader che nessun asset usa
(nemmeno URP Lit), quindi la creazione del livello si fermava prima della camera.

## Comandi
| Tasto | Azione |
|---|---|
| W A S D | muoversi |
| Shift | correre |
| Spazio | saltare |
| Ctrl sinistro (tenuto) | accovacciarsi (più lento; ti rialzi solo se c'è spazio) |
| Clic sinistro | sparare (pistola: un colpo per clic; AK-47: tieni premuto; AWP: un colpo, poi 1,2 s di otturatore) |
| Clic destro (tenuto) | mirare con le mire metalliche allineate al centro (il mirino dell HUD sparisce); con l AWP = cannocchiale (girare la rotella = zoom 4x-12x) |
| Rotella (tenuta) | afferrare un Kekko (vivo o morto, cervello ON o OFF) o un pezzo fino a 60 m e portarlo dove vuoi; girare la rotella = avvicina/allontana |
| R | ricaricare |
| TAB | mostra / nasconde il monitor in alto a sinistra |
| T | raggi X (ossa, organi, arterie, vene, nervi) |
| Y | nuovo Kekko nel punto indicato dal mirino (a terra, rivolto verso di te) |
| N | nuovo Kekko in un punto casuale |
| [ ] | rallenta / accelera il tempo |
| P | pausa |
| Backspace | ricomincia (anche pulsante RESET nel menu Esc) |
| Esc | menu: scegli PISTOLA, AK-47, AWP o COLTELLO, SENSIBILITA mouse, SENSIBILITA ADS (default 0,7x) e VOLUME (salvati), RESET, KEKKO BRAINS on/off (off = stanno fermi finché non vengono presi di mira), riprendi |

Sotto il mirino c'è scritto cosa stai puntando; una X rossa conferma che il colpo ha preso un corpo.

## Cosa fa
- SUPERMARKET esterno: parcheggio, strada, auto parcheggiate con Kekko vicino e autisti seduti dentro; post-processing (ACES, bloom, vignetta).
- Mappe (menu Esc, pulsante MAP, riavvia): TEST SITE; SUPERMARKET (Fresh Mart: corsie con scaffali pieni di prodotti e cartelli, freezer, 3 casse con cassiere che restano alla cassa finché non si spaventano, clienti che girano, carrelli, frutta); HALLOWEEN NIGHT (notte con nebbia viola e luna, casa stregata con finestre illuminate e torretta, cimitero con lapidi/croci/fossa aperta con bara e recinto di ferro, alberi morti, zucche intagliate con candele che tremolano, lampioni, spaventapasseri, balle di fieno, pozzo, pipistrelli).
- Coltello: clic sinistro = fendente (portata 1,8 m, alterna destra/sinistra). Taglia una linea sulla pelle che sanguina; alla gola recide la carotide (morte in 6-14 s); tagli ripetuti staccano mano/avambraccio/piede (5) o braccio/coscia (8).
- Tagliare un Kekko a metà: bisogna distruggere la pancia tutto attorno alla vita, caricatore dopo caricatore (circa 4 caricatori di pistola, 2 di AK, 7 colpi di AWP se arrivano tutti vicino alla vita). Ogni colpo lì strappa fori sempre più grandi e fa volare più carne; a metà strada l'addome risulta "SHREDDED", alla fine il busto si stacca dal bacino.
- Sangue scuro (rosso quasi bordeaux, pozze più scure); le strisciate e le impronte sono più chiare e trasparenti, come un velo sottile.
- Colpi: ogni proiettile viene provato sulla pelle esattamente come è disegnata (skinning calcolato sulla CPU con le stesse ossa della GPU) e sulla forma solida del corpo; vince il più vicino, prima di muri e pavimento. Sotto il mirino LAST SHOT dice cosa hai colpito e a che distanza (oppure MISS e cosa c'era dietro).
- Sangue che si sbava: camminando in una pozza lasci impronte sempre più deboli (anche i Carl); corpi trascinati, che strisciano o scivolano spalmano il sangue e, se sanguinano, lasciano la scia.
- Torri per i drop test sul muro di fondo (dietro la partenza): 5, 8, 10 e 15 m, con tacche ogni metro. Sali sulla pedana arancione davanti: ti porta in cima (e giù); si chiama anche da sotto o da sopra. Con il tasto centrale puoi tirare su i Carl.
- Scale normali (alzata 18 cm, pedata 30 cm) per le due piattaforme; i Kekko le usano camminando e strisciando, e cadono se non hanno niente sotto i piedi.
- Armi: pistola 9 mm (15), AK-47 7.62 (30; in testa grosso foro d uscita, niente esplosione), AWP .338 Lapua (10, otturatore, cannocchiale; stacca un arto in un colpo, esplode sempre la testa, attraversa un corpo e colpisce quello dietro).
- Il sangue non sparisce: ~24 000 macchie per tipo, 8 000 sui corpi, fino a 2 000 pozze (poi si allargano quelle esistenti).
- Manichini con ragdoll vero (Rigidbody + CharacterJoint): camminano, scappano quando spari,
  si tengono la ferita, cadono se colpiti alle gambe, svengono e muoiono dissanguati.
- Colpo in testa = morte istantanea; secondo colpo in testa = decapitazione.
- Arti: si staccano al 2° colpo (o 30% al 1°): il pezzo diventa un corpo fisico separato,
  il moncone spruzza sangue a getti pulsanti, volano pezzi di carne.
- Sangue: gocce balistiche, macchie tonde/allungate secondo velocità e angolo d'impatto,
  colature sui muri, macchie sui corpi, pozze che si allargano col volume.
- HUD: SPECIMENS, TIME, munizioni, monitor medico quando guardi un manichino.

## Novità
- Corpo a segmenti lisci come l'originale (bacino, torace, testa, braccia, avambracci+mani, cosce, stinchi+piedi),
  generati da codice; la carne si strappa dove escono i proiettili e sotto si vede l'interno.
- Gli arti si tagliano nel punto colpito (3° colpo, 2° al 50%, o troppa carne persa); i pezzi cadono con la fisica.
- Il sangue cola sulla pelle da una parte all'altra del corpo e gocciola a terra; pozze sotto i corpi.
- 6 muri COVER / 01-06: i Kekko ci corrono dietro, o ci strisciano se colpiti alle gambe.
- Si possono colpire e tagliare anche i corpi a terra; i pezzi di carne non fermano i proiettili.

- T = raggi X: pelle trasparente, si vedono ossa, organi, arterie (rosse), vene (blu), nervi (gialli).

## Se qualcosa non va
Copia gli errori della **Console** e mandameli. Il codice non è stato compilato qui
(nell'ambiente cloud non c'è Unity).
Per una build standalone aggiungi `Universal Render Pipeline/Lit` e `Sprites/Default`
in Project Settings → Graphics → Always Included Shaders.

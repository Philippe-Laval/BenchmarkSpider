# BenchmarkSpider

Outil en ligne de commande, port C# du script Python `evaluation.py` du benchmark Spider (text-to-SQL). Il calcule les scores d'évaluation de requêtes prédites par rapport à des requêtes de référence (gold).

## Utilisation

```
BenchmarkSpider --gold <fichier> --pred <fichier> --db <répertoire> --table <tables.json> --etype <all|exec|match>
```

Avec le SDK .NET :

```
dotnet run --project BenchmarkSpider -- --gold dev_gold.sql --pred pred.sql --db spider/database --table spider/tables.json --etype all
```

Sans argument, l'outil affiche les statistiques générales du jeu de données Spider.

## Paramètres

Tous les paramètres sont obligatoires.

| Paramètre | Description |
|-----------|-------------|
| `--gold` | Fichier des requêtes de référence. Une ligne par exemple, au format `SQL<TAB>db_id`. |
| `--pred` | Fichier des requêtes prédites, dans le même ordre que le fichier gold. Seule la première colonne (séparée par tabulation) est lue. |
| `--db` | Répertoire des bases SQLite, organisé en `<db_id>/<db_id>.sqlite`. Le schéma est lu directement depuis ces fichiers. |
| `--table` | Fichier `tables.json` de Spider, utilisé pour construire la carte des clés étrangères. |
| `--etype` | Type d'évaluation : `all`, `exec` ou `match`. |

### Valeurs de `--etype`

- `exec` : précision d'exécution (les résultats des deux requêtes sont comparés sur la base SQLite).
- `match` : correspondance exacte et partielle (précision, rappel, F1 par composant SQL).
- `all` : les deux évaluations.

## Sortie

Les scores sont affichés par niveau de difficulté (`easy`, `medium`, `hard`, `extra`) et pour l'ensemble (`all`).

Pendant l'évaluation, l'outil écrit aussi sur la console :

- `eval_err_num:N` pour chaque requête prédite impossible à analyser (comptée comme vide) ;
- les requêtes prédite et de référence pour chaque exemple sans correspondance exacte.

## Codes de sortie

| Code | Signification |
|------|---------------|
| 0 | Succès. |
| 2 | Argument manquant, inconnu ou incomplet, ou `--etype` invalide. |

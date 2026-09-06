import 'dart:convert';

import 'package:path/path.dart' as p;
import 'package:shared_preferences/shared_preferences.dart';
import 'package:sqflite/sqflite.dart';

import '../core/api_client.dart';

class OfflineSaleQueue {
  static const _legacyKey = 'offline_pos_sales_v1';
  static const _dbName = 'findupto_pos_queue.db';
  static const _table = 'offline_sales';

  Database? _db;

  Future<Database> get _database async {
    if (_db != null) return _db!;
    final path = p.join(await getDatabasesPath(), _dbName);
    _db = await openDatabase(
      path,
      version: 1,
      onCreate: (db, _) async {
        await db.execute('''
          CREATE TABLE $_table (
            client_operation_id TEXT PRIMARY KEY,
            payload TEXT NOT NULL,
            state TEXT NOT NULL DEFAULT 'pending',
            attempts INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL,
            updated_at INTEGER NOT NULL,
            last_error TEXT
          )
        ''');
        await db.execute('CREATE INDEX idx_offline_sales_state_created ON $_table(state, created_at)');
      },
    );
    await _migrateLegacyQueue(_db!);
    return _db!;
  }

  Future<void> _migrateLegacyQueue(Database db) async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getStringList(_legacyKey);
    if (raw == null || raw.isEmpty) return;

    await db.transaction((txn) async {
      for (final encoded in raw) {
        try {
          final sale = Map<String, dynamic>.from(jsonDecode(encoded) as Map);
          final id = '${sale['clientOperationId'] ?? ''}'.trim();
          if (id.isEmpty) continue;
          final now = DateTime.now().millisecondsSinceEpoch;
          await txn.insert(
            _table,
            {
              'client_operation_id': id,
              'payload': jsonEncode(sale),
              'state': 'pending',
              'attempts': 0,
              'created_at': now,
              'updated_at': now,
            },
            conflictAlgorithm: ConflictAlgorithm.ignore,
          );
        } catch (_) {
          // Ignore one corrupt legacy entry; valid queued sales must still migrate.
        }
      }
    });
    await prefs.remove(_legacyKey);
  }

  Future<List<Map<String, dynamic>>> load() async {
    final db = await _database;
    final rows = await db.query(
      _table,
      where: 'state = ?',
      whereArgs: ['pending'],
      orderBy: 'created_at ASC',
    );
    return rows
        .map((row) => Map<String, dynamic>.from(jsonDecode(row['payload']! as String) as Map))
        .toList();
  }

  Future<void> enqueue(Map<String, dynamic> sale) async {
    final id = '${sale['clientOperationId'] ?? ''}'.trim();
    if (id.isEmpty) throw ArgumentError('Offline sale requires clientOperationId');
    final db = await _database;
    final now = DateTime.now().millisecondsSinceEpoch;
    await db.insert(
      _table,
      {
        'client_operation_id': id,
        'payload': jsonEncode(sale),
        'state': 'pending',
        'attempts': 0,
        'created_at': now,
        'updated_at': now,
      },
      conflictAlgorithm: ConflictAlgorithm.ignore,
    );
  }

  Future<int> count() async {
    final db = await _database;
    final result = await db.rawQuery('SELECT COUNT(*) AS count FROM $_table WHERE state = ?', ['pending']);
    return (result.single['count'] as int?) ?? 0;
  }

  Future<SyncQueueResult> sync(PosApiClient api) async {
    final db = await _database;
    final pending = await load();
    if (pending.isEmpty) return const SyncQueueResult(0, 0, 0);

    var completed = 0;
    var conflicts = 0;

    for (var start = 0; start < pending.length; start += 100) {
      final batch = pending.skip(start).take(100).toList();
      final ids = batch.map((x) => '${x['clientOperationId']}').toList();
      await db.transaction((txn) async {
        final now = DateTime.now().millisecondsSinceEpoch;
        for (final id in ids) {
          await txn.rawUpdate(
            'UPDATE $_table SET attempts = attempts + 1, updated_at = ? WHERE client_operation_id = ? AND state = ?',
            [now, id, 'pending'],
          );
        }
      });

      try {
        final response = await api.syncPushOrders(batch);
        final results = (response['results'] as List?) ?? const [];
        await db.transaction((txn) async {
          final now = DateTime.now().millisecondsSinceEpoch;
          for (var i = 0; i < batch.length; i++) {
            final result = i < results.length && results[i] is Map
                ? Map<String, dynamic>.from(results[i] as Map)
                : const <String, dynamic>{};
            final id = ids[i];
            if (result['success'] == true) {
              completed++;
              await txn.update(_table, {'state': 'completed', 'updated_at': now}, where: 'client_operation_id = ?', whereArgs: [id]);
            } else if (result['conflict'] == true) {
              conflicts++;
              await txn.update(_table, {
                'state': 'conflict',
                'updated_at': now,
                'last_error': '${result['message'] ?? 'Synchronization conflict'}',
              }, where: 'client_operation_id = ?', whereArgs: [id]);
            } else {
              await txn.update(_table, {
                'last_error': '${result['message'] ?? 'Synchronization failed'}',
                'updated_at': now,
              }, where: 'client_operation_id = ?', whereArgs: [id]);
            }
          }
        });
      } catch (error) {
        final now = DateTime.now().millisecondsSinceEpoch;
        await db.update(_table, {'last_error': '$error', 'updated_at': now}, where: 'state = ? AND client_operation_id IN (${List.filled(ids.length, '?').join(',')})', whereArgs: ['pending', ...ids]);
        break;
      }
    }

    return SyncQueueResult(completed, await count(), conflicts);
  }

  Future<void> dispose() async {
    final db = _db;
    _db = null;
    await db?.close();
  }
}

class SyncQueueResult {
  const SyncQueueResult(this.completed, this.pending, this.conflicts);
  final int completed;
  final int pending;
  final int conflicts;
}

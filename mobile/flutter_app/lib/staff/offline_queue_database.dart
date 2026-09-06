import 'dart:convert';
import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';

class OfflineQueueDatabase {
  OfflineQueueDatabase._(this._db);

  final Database _db;

  static Future<OfflineQueueDatabase> open() async {
    final root = await getDatabasesPath();
    final db = await openDatabase(
      p.join(root, 'findupto_pos_offline.db'),
      version: 1,
      onCreate: (db, version) async {
        await db.execute('''
          CREATE TABLE offline_sales (
            client_operation_id TEXT PRIMARY KEY,
            payload TEXT NOT NULL,
            state TEXT NOT NULL DEFAULT 'pending',
            attempts INTEGER NOT NULL DEFAULT 0,
            last_error TEXT,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
          )
        ''');
        await db.execute('CREATE INDEX idx_offline_sales_state_created ON offline_sales(state, created_at_utc)');
      },
    );
    return OfflineQueueDatabase._(db);
  }

  Future<void> enqueue(Map<String, dynamic> sale) async {
    final id = sale['clientOperationId']?.toString().trim();
    if (id == null || id.isEmpty) {
      throw ArgumentError('Offline sale requires clientOperationId');
    }
    final now = DateTime.now().toUtc().toIso8601String();
    await _db.insert(
      'offline_sales',
      {
        'client_operation_id': id,
        'payload': jsonEncode(sale),
        'state': 'pending',
        'attempts': 0,
        'created_at_utc': now,
        'updated_at_utc': now,
      },
      conflictAlgorithm: ConflictAlgorithm.ignore,
    );
  }

  Future<List<Map<String, dynamic>>> pending({int limit = 100}) async {
    final rows = await _db.query(
      'offline_sales',
      where: 'state = ?',
      whereArgs: ['pending'],
      orderBy: 'created_at_utc ASC',
      limit: limit,
    );
    return rows.map((row) => Map<String, dynamic>.from(jsonDecode(row['payload']! as String) as Map)).toList();
  }

  Future<int> count() async {
    final result = await _db.rawQuery("SELECT COUNT(*) AS count FROM offline_sales WHERE state = 'pending'");
    return (result.first['count'] as int?) ?? 0;
  }

  Future<void> markCompleted(Iterable<String> ids) async {
    final batch = _db.batch();
    for (final id in ids) {
      batch.update('offline_sales', {'state': 'completed', 'updated_at_utc': DateTime.now().toUtc().toIso8601String()}, where: 'client_operation_id = ?', whereArgs: [id]);
    }
    await batch.commit(noResult: true);
  }

  Future<void> markConflict(String id, String error) async {
    await _db.update(
      'offline_sales',
      {
        'state': 'conflict',
        'last_error': error.substring(0, error.length.clamp(0, 1000)),
        'updated_at_utc': DateTime.now().toUtc().toIso8601String(),
      },
      where: 'client_operation_id = ?',
      whereArgs: [id],
    );
  }

  Future<void> markAttempt(String id, String? error) async {
    await _db.rawUpdate(
      'UPDATE offline_sales SET attempts = attempts + 1, last_error = ?, updated_at_utc = ? WHERE client_operation_id = ?',
      [error, DateTime.now().toUtc().toIso8601String(), id],
    );
  }

  Future<void> close() => _db.close();
}

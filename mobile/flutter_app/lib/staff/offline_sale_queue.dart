import 'dart:convert';
import 'dart:io';

import 'package:path/path.dart' as p;
import 'package:shared_preferences/shared_preferences.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

import '../core/api_client.dart';

class OfflineSaleQueue {
  static const _legacyKey = 'offline_pos_sales_v1';
  static const _dbName = 'findupto_pos_queue.db';
  static const _table = 'offline_sales';

  Database? _db;
  bool _factoryInitialized = false;

  Future<Database> get _database async {
    if (_db != null) return _db!;
    _initializeDatabaseFactory();
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

  void _initializeDatabaseFactory() {
    if (_factoryInitialized) return;
    _factoryInitialized = true;
    if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
      sqfliteFfiInit();
      databaseFactory = databaseFactoryFfi;
    }
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
          await txn.insert(_table, {
            'client_operation_id': id,
            'payload': jsonEncode(sale),
            'state': 'pending',
            'attempts': 0,
            'created_at': now,
            'updated_at': now,
          }, conflictAlgorithm: ConflictAlgorithm.ignore);
        } catch (_) {}
      }
    });
    await prefs.remove(_legacyKey);
  }

  Future<List<Map<String, dynamic>>> load() async {
    final db = await _database;
    final rows = await db.query(_table, where: 'state = ?', whereArgs: ['pending'], orderBy: 'created_at ASC');
    return rows.map((row) {
      final payload = row['payload'];
      if (payload is! String) throw StateError('Offline sale payload is invalid');
      return Map<String, dynamic>.from(jsonDecode(payload) as Map);
    }).toList();
  }

  Future<void> enqueue(Map<String, dynamic> sale) async {
    final id = '${sale['clientOperationId'] ?? ''}'.trim();
    if (id.isEmpty) throw ArgumentError('Offline sale requires clientOperationId');
    final db = await _database;
    final now = DateTime.now().millisecondsSinceEpoch;
    await db.insert(_table, {
      'client_operation_id': id,
      'payload': jsonEncode(sale),
      'state': 'pending',
      'attempts': 0,
      'created_at': now,
      'updated_at': now,
    }, conflictAlgorithm: ConflictAlgorithm.ignore);
  }

  Future<int> count() async {
    final db = await _database;
    final result = await db.rawQuery('SELECT COUNT(*) AS count FROM $_table WHERE state = ?', ['pending']);
    return (result.single['count'] as num?)?.toInt() ?? 0;
  }

  Future<void> _markAttempted(Database db, String id) async {
    await db.rawUpdate(
      'UPDATE $_table SET attempts = attempts + 1, updated_at = ? WHERE client_operation_id = ? AND state = ?',
      [DateTime.now().millisecondsSinceEpoch, id, 'pending'],
    );
  }

  Future<void> _markCompleted(Database db, String id) async {
    await db.update(_table, {
      'state': 'completed',
      'updated_at': DateTime.now().millisecondsSinceEpoch,
      'last_error': null,
    }, where: 'client_operation_id = ?', whereArgs: [id]);
  }

  Future<void> _markConflict(Database db, String id, String message) async {
    await db.update(_table, {
      'state': 'conflict',
      'updated_at': DateTime.now().millisecondsSinceEpoch,
      'last_error': message,
    }, where: 'client_operation_id = ?', whereArgs: [id]);
  }

  Future<void> _markError(Database db, String id, Object error) async {
    await db.update(_table, {
      'last_error': '$error',
      'updated_at': DateTime.now().millisecondsSinceEpoch,
    }, where: 'client_operation_id = ?', whereArgs: [id]);
  }

  Future<SyncQueueResult> sync(PosApiClient api) async {
    final db = await _database;
    final pending = await load();
    if (pending.isEmpty) return const SyncQueueResult(0, 0, 0);

    var completed = 0;
    var conflicts = 0;

    for (final sale in pending) {
      final id = '${sale['clientOperationId'] ?? ''}';
      await _markAttempted(db, id);
      try {
        final order = await api.createStaffOrder(
          customerId: (sale['customerId'] as num?)?.toInt(),
          tableId: (sale['tableId'] as num?)?.toInt(),
          orderType: '${sale['orderType'] ?? 'Counter'}',
          notes: '${sale['notes'] ?? ''}',
          clientOperationId: id,
          items: List<Map<String, dynamic>>.from((sale['items'] as List?) ?? const []),
        );
        final orderId = (order['id'] as num?)?.toInt();
        if (orderId == null) throw StateError('Server did not return an order id');

        final paymentMethod = '${sale['paymentMethod'] ?? ''}'.trim();
        if (paymentMethod.isNotEmpty) {
          await api.collectPayment(
            orderId,
            amountTendered: (sale['amountTendered'] as num?)?.toDouble() ?? (order['total'] as num?)?.toDouble() ?? 0,
            method: paymentMethod,
            reference: '${sale['paymentReference'] ?? ''}',
            clientOperationId: '${sale['paymentClientOperationId'] ?? '$id:payment'}',
          );
        }
        await _markCompleted(db, id);
        completed++;
      } catch (error) {
        final message = '$error';
        if (message.toLowerCase().contains('conflict') || message.contains('409')) {
          await _markConflict(db, id, message);
          conflicts++;
        } else {
          await _markError(db, id, error);
          break;
        }
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

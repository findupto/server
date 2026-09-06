import 'dart:convert';
import 'package:shared_preferences/shared_preferences.dart';
import '../core/api_client.dart';

class OfflineSaleQueue {
  static const _key = 'offline_pos_sales_v1';

  Future<List<Map<String, dynamic>>> load() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getStringList(_key) ?? const [];
    return raw.map((x) => Map<String, dynamic>.from(jsonDecode(x) as Map)).toList();
  }

  Future<void> enqueue(Map<String, dynamic> sale) async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getStringList(_key) ?? <String>[];
    raw.add(jsonEncode(sale));
    await prefs.setStringList(_key, raw);
  }

  Future<int> count() async => (await load()).length;

  Future<SyncQueueResult> sync(PosApiClient api) async {
    final pending = await load();
    if (pending.isEmpty) return const SyncQueueResult(0, 0, 0);
    final remaining = <Map<String, dynamic>>[];
    var completed = 0;
    var conflicts = 0;
    try {
      for (var start = 0; start < pending.length; start += 100) {
        final batch = pending.skip(start).take(100).toList();
        final response = await api.syncPushOrders(batch);
        final results = (response['results'] as List?) ?? const [];
        for (var i = 0; i < batch.length; i++) {
          final result = i < results.length ? Map<String, dynamic>.from(results[i] as Map) : const <String, dynamic>{};
          if (result['success'] == true) {
            completed++;
          } else if (result['conflict'] == true) {
            conflicts++;
          } else {
            remaining.add(batch[i]);
          }
        }
      }
    } catch (_) {
      remaining.addAll(pending.where((sale) => !remaining.any((x) => x['clientOperationId'] == sale['clientOperationId'])));
    }
    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(_key, remaining.map(jsonEncode).toList());
    return SyncQueueResult(completed, remaining.length, conflicts);
  }
}

class SyncQueueResult {
  const SyncQueueResult(this.completed, this.pending, this.conflicts);
  final int completed;
  final int pending;
  final int conflicts;
}

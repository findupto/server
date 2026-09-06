import '../core/api_client.dart';

class PosApiAdapter {
  PosApiAdapter(this.api);
  final PosApiClient api;

  Future<List<Map<String, dynamic>>> products() async =>
      (await api.staffOrders()).cast<Map<String, dynamic>>();

  Future<Map<String, dynamic>> createSale({
    int? customerId,
    int? tableId,
    required List<Map<String, dynamic>> items,
    String orderType = 'Counter',
    String notes = '',
    String? clientOperationId,
  }) => api.createStaffOrder(
        customerId: customerId,
        items: items,
        orderType: orderType,
        notes: notes,
      );

  Future<Map<String, dynamic>> pay({
    required int orderId,
    required double amountTendered,
    required String method,
    String reference = '',
  }) => api.collectPayment(
        orderId,
        amountTendered: amountTendered,
        method: method,
        reference: reference,
      );

  Future<List<dynamic>> tables() => api.getList('/api/tables?active=true');

  Future<List<dynamic>> printers() => api.getList('/api/printers/discover');

  Future<dynamic> selectPrinter(String documentType) =>
      api.post('/api/printers/select', {'documentType': documentType});

  Future<dynamic> openDrawer(double openingFloat) =>
      api.post('/api/cash-drawer/open', {'openingFloat': openingFloat});

  Future<List<dynamic>> syncPull({String? since}) =>
      api.getList('/api/sync/pull${since == null ? '' : '?since=${Uri.encodeQueryComponent(since)}'}');
}

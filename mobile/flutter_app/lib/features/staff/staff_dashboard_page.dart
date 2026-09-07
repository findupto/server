import 'package:flutter/material.dart';

import '../../core/api_client.dart';
import 'ai_configuration_page.dart';
import 'ai_operator_page.dart';
import 'delivery_map_page.dart';
import 'product_management_page.dart';
import 'rider_tracking_page.dart';

class StaffDashboardPage extends StatelessWidget {
  const StaffDashboardPage({super.key, required this.api, required this.role});

  final PosApiClient api;
  final String role;

  @override
  Widget build(BuildContext context) {
    final r = role.toLowerCase();
    final canManageProducts = r == 'owner' || r == 'manager' || r == 'admin';
    final canUseAi = r == 'owner' || r == 'manager' || r == 'admin' || r == 'counter';
    final actions = <_Action>[
      if (canUseAi) const _Action('AI Operator', Icons.auto_awesome, 'AI_OPERATOR'),
      if (r == 'owner') const _Action('AI Configuration', Icons.settings_suggest, 'AI_CONFIG'),
      if (r == 'rider') const _Action('Rider GPS', Icons.my_location, 'RIDER_GPS'),
      if (r != 'rider' && r != 'kitchen' && r != 'waiter')
        const _Action('Live Rider Map', Icons.map, 'DELIVERY_MAP'),
      if (r == 'kitchen') const _Action('Kitchen Orders', Icons.restaurant, '/api/kitchen/orders'),
      if (r == 'waiter') const _Action('Waiter Orders', Icons.room_service, '/api/waiter/orders'),
      if (r == 'rider') const _Action('Deliveries', Icons.delivery_dining, '/api/rider/orders'),
      if (r != 'kitchen' && r != 'waiter' && r != 'rider') ...[
        const _Action('Orders', Icons.receipt_long, '/api/orders'),
        const _Action('Payments', Icons.payments, '/api/orders'),
        if (canManageProducts)
          const _Action('Products', Icons.inventory_2, 'PRODUCT_MANAGEMENT'),
        const _Action('Promotions', Icons.local_offer, '/api/promotions/all'),
        const _Action('Staff', Icons.groups, '/api/users'),
      ],
    ];

    return Scaffold(
      appBar: AppBar(title: Text('$role Dashboard')),
      body: GridView.builder(
        padding: const EdgeInsets.all(16),
        gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
          maxCrossAxisExtent: 240,
          mainAxisExtent: 130,
          crossAxisSpacing: 12,
          mainAxisSpacing: 12,
        ),
        itemCount: actions.length,
        itemBuilder: (_, i) {
          final action = actions[i];
          return Card(
            child: InkWell(
              onTap: () {
                if (action.path == 'PRODUCT_MANAGEMENT') {
                  Navigator.push(
                    context,
                    MaterialPageRoute(builder: (_) => ProductManagementPage(api: api)),
                  );
                } else if (action.path == 'AI_OPERATOR') {
                  Navigator.push(
                    context,
                    MaterialPageRoute(builder: (_) => AiOperatorPage(api: api, role: role)),
                  );
                } else if (action.path == 'AI_CONFIG') {
                  Navigator.push(
                    context,
                    MaterialPageRoute(builder: (_) => AiConfigurationPage(api: api)),
                  );
                } else if (action.path == 'RIDER_GPS') {
                  Navigator.push(
                    context,
                    MaterialPageRoute(builder: (_) => RiderTrackingPage(api: api)),
                  );
                } else if (action.path == 'DELIVERY_MAP') {
                  Navigator.push(
                    context,
                    MaterialPageRoute(builder: (_) => DeliveryMapPage(api: api)),
                  );
                } else {
                  Navigator.push(
                    context,
                    MaterialPageRoute(
                      builder: (_) => StaffListPage(
                        api: api,
                        title: action.title,
                        path: action.path,
                      ),
                    ),
                  );
                }
              },
              child: Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(action.icon, size: 34),
                    const SizedBox(height: 8),
                    Text(action.title),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class StaffListPage extends StatefulWidget {
  const StaffListPage({super.key, required this.api, required this.title, required this.path});

  final PosApiClient api;
  final String title;
  final String path;

  @override
  State<StaffListPage> createState() => _StaffListPageState();
}

class _StaffListPageState extends State<StaffListPage> {
  dynamic data;
  String? error;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final value = await widget.api.getList(widget.path);
      if (mounted) setState(() => data = value);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.title)),
      body: error != null
          ? Center(child: Text(error!))
          : data == null
              ? const Center(child: CircularProgressIndicator())
              : ListView.builder(
                  itemCount: data is List ? data.length : 1,
                  itemBuilder: (_, i) {
                    final value = data is List ? data[i] : data;
                    final title = value is Map
                        ? (value['name'] ?? value['title'] ?? value['id']?.toString() ?? 'Item').toString()
                        : value.toString();
                    final subtitle = value is Map
                        ? value.entries.take(4).map((e) => '${e.key}: ${e.value}').join(' • ')
                        : '';
                    return ListTile(title: Text(title), subtitle: Text(subtitle));
                  },
                ),
    );
  }
}

class _Action {
  const _Action(this.title, this.icon, this.path);

  final String title;
  final IconData icon;
  final String path;
}
